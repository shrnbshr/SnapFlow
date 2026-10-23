// GPL-3.0-or-later. A small visual cover fades the toolbar while its native buttons stay interactive.
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ShareX.Roll;

internal sealed class PreviewZoomFade : Control
{
    private readonly PreviewCanvas viewport;
    private readonly PreviewZoomBar toolbar;
    private readonly UiTween opacity;
    private readonly System.Windows.Forms.Timer hideDelay = new() { Interval = 300 };
    private readonly ImageAttributes blend = new();
    private readonly ColorMatrix alpha = new();
    private Bitmap? snapshot;
    private bool shown, dirty = true;
    internal float Opacity => opacity.Value;
    internal bool IsAnimating => opacity.IsRunning;

    internal PreviewZoomFade(PreviewCanvas viewport, PreviewZoomBar toolbar)
    {
        this.viewport = viewport;
        this.toolbar = toolbar;
        TabStop = false;
        AccessibleRole = AccessibleRole.None;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        // Opacity never changes layout, and its native drawing resources are reused per frame.
        // The viewport remains visible when the visual cover hides at an animation endpoint.
        opacity = new UiTween(viewport, ApplyOpacity);
        hideDelay.Tick += (_, _) => { hideDelay.Stop(); ApplyShown(false); };
        foreach (Control control in new Control[] { toolbar }.Concat(toolbar.Controls.Cast<Control>()))
            control.Invalidated += (_, _) => RefreshSnapshot();
        toolbar.SizeChanged += (_, _) => RefreshSnapshot();
        toolbar.LocationChanged += (_, _) => RefreshSnapshot();
    }

    internal void SetShown(bool value)
    {
        if (!value && shown && viewport.Visible && viewport.Image != null && UiMotion.Enabled)
        {
            // Give a brief return window without restarting the delay on repeated leave notifications.
            if (!hideDelay.Enabled) hideDelay.Start();
            return;
        }
        hideDelay.Stop();
        ApplyShown(value);
    }

    private void ApplyShown(bool value)
    {
        float target = value ? 1 : 0;
        if (shown == value && (opacity.IsRunning || opacity.Value == target)) return;
        shown = value;
        if (!UiMotion.Enabled || !viewport.Visible || !viewport.IsHandleCreated || toolbar.Width < 1 || toolbar.Height < 1)
        {
            opacity.SnapTo(target);
            return;
        }
        Bounds = toolbar.Bounds;
        dirty = true;
        toolbar.Show();
        Show();
        BringToFront();
        // Keep velocity through rapid enter/leave. A gentler arrival and slightly quicker exit
        // settle without decorative bounce; input remains available throughout the transition.
        opacity.SpringTo(target, value ? 260 : 230);
    }

    internal void HideImmediately()
    {
        hideDelay.Stop();
        shown = false;
        opacity.SnapTo(0);
    }

    internal void RefreshSnapshot()
    {
        dirty = true;
        if (Visible) { Bounds = toolbar.Bounds; Invalidate(); }
    }

    private void ApplyOpacity()
    {
        if (IsDisposed) return;
        // A reversing spring can touch its previous boundary while moving toward the new target.
        // Hand off to the real toolbar only at the endpoint that is currently requested.
        if ((shown && opacity.Value == 1) || (!shown && opacity.Value == 0))
        {
            toolbar.Visible = opacity.Value == 1 && viewport.Image != null;
            Hide();
            snapshot?.Dispose(); snapshot = null;
        }
        else Invalidate();
    }

    protected override void WndProc(ref Message message)
    {
        // Match PageTransition: clicks and wheel input reach the live native controls underneath.
        if (message.Msg == 0x84) { message.Result = new IntPtr(-1); return; }
        base.WndProc(ref message);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        viewport.PaintZoomBackdrop(e.Graphics, Bounds);
        if (opacity.Value <= 0) return;
        if (dirty || snapshot == null || snapshot.Size != Size)
        {
            if (snapshot == null || snapshot.Size != Size)
            {
                snapshot?.Dispose(); snapshot = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            }
            toolbar.DrawToBitmap(snapshot, ClientRectangle);
            dirty = false;
        }
        GraphicsState state = e.Graphics.Save();
        if (toolbar.Region != null) e.Graphics.SetClip(toolbar.Region, CombineMode.Intersect);
        alpha.Matrix33 = opacity.Value;
        blend.SetColorMatrix(alpha);
        e.Graphics.DrawImage(snapshot, ClientRectangle, 0, 0, Width, Height, GraphicsUnit.Pixel, blend);
        e.Graphics.Restore(state);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { hideDelay.Dispose(); opacity.Dispose(); blend.Dispose(); snapshot?.Dispose(); snapshot = null; }
        base.Dispose(disposing);
    }
}
