// GPL-3.0-or-later. Native checkbox semantics with a short switch animation.
using System.Drawing.Drawing2D;

namespace ShareX.Roll;

internal sealed class MotionToggle : CheckBox
{
    private readonly UiTween position;
    private readonly UiTween hover, press;
    private CapturePalette palette = CapturePalette.Light;
    internal CapturePalette Palette
    {
        get => palette;
        set { palette = value; ButtonPointerBounds.ApplyToggle(this, palette); }
    }

    internal static RectangleF TrackBounds(Size size, float scale) =>
        new(2 * scale, (size.Height - 24 * scale) / 2, size.Width - 4 * scale, 24 * scale);

    internal MotionToggle()
    {
        AutoSize = false;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        position = new(this, Invalidate);
        hover = new(this, Invalidate);
        press = new(this, Invalidate);
    }

    protected override void OnMouseEnter(EventArgs e) { hover.AnimateTo(1, 100); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover.AnimateTo(0, 100); press.SnapTo(0); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (Enabled && e.Button == MouseButtons.Left) press.SnapTo(1); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { press.AnimateTo(0, 100); base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) press.AnimateTo(0, 100); base.OnMouseCaptureChanged(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) press.SnapTo(1); base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { press.AnimateTo(0, 100); base.OnKeyUp(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { press.SnapTo(0); base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { hover?.SnapTo(0); press?.SnapTo(0); base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        position?.SpringTo(Checked ? 1 : 0, 160);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        CapturePalette p = Palette;
        float s = DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? p.Surface);
        RectangleF bounds = TrackBounds(ClientSize, s);
        float radius = p.CornerRadius(12) * s;
        if (p.IsMaterial) CaptureDrawing.InsetAntialiasedFace(ref bounds, ref radius);
        using GraphicsPath track = CaptureDrawing.Rounded(bounds, radius);
        Color trackColor, thumbColor;
        if (p.IsWinUI || p.IsMetro)
        {
            Color off = CaptureDrawing.Blend(p.Canvas, p.Hover, hover.Value);
            trackColor = Enabled ? CaptureDrawing.Blend(off, p.Accent, position.Value) : p.Surface;
            thumbColor = Enabled ? CaptureDrawing.Blend(p.Secondary, p.AccentText, position.Value) : p.FieldBorder;
        }
        else if (p.IsMaterial)
        {
            trackColor = CaptureDrawing.Blend(p.RaisedSurface, p.Accent, position.Value);
            thumbColor = CaptureDrawing.Blend(p.FieldBorder, p.AccentText, position.Value);
            if (Enabled) trackColor = CaptureDrawing.Blend(trackColor, thumbColor, hover.Value * .08f);
            else
            {
                trackColor = CaptureDrawing.Blend(trackColor, p.Surface, .8f);
                thumbColor = CaptureDrawing.Blend(p.Secondary, p.Surface, .40f);
            }
        }
        else if (p.IsClaude)
        {
            Color off = CaptureDrawing.Blend(p.Surface, p.Hover, Enabled ? hover.Value * .4f : 0);
            trackColor = Enabled ? CaptureDrawing.Blend(off, p.Accent, position.Value) : off;
            thumbColor = Enabled ? CaptureDrawing.Blend(p.Secondary, p.AccentText, position.Value) :
                CaptureDrawing.Blend(p.FieldBorder, p.Surface, .38f);
        }
        else
        {
            Color off = CaptureDrawing.Blend(p.Secondary, p.Surface, .12f);
            trackColor = CaptureDrawing.Blend(off, p.Accent, position.Value);
            if (!Enabled) trackColor = CaptureDrawing.Blend(trackColor, p.Surface, .45f);
            trackColor = CaptureDrawing.Blend(trackColor, p.Text, hover.Value * .06f);
            thumbColor = CaptureDrawing.Blend(p.Surface, p.AccentText, position.Value);
        }
        using SolidBrush fill = new(trackColor);
        g.FillPath(fill, track);
        float diameter = (p.IsWinUI ? 12 : p.IsMaterial ? 12 + 6 * position.Value : 18) * s;
        float thumbWidth = diameter + 3 * s * press.Value;
        float inset = 3 * s + (18 * s - diameter) / 2;
        float x = bounds.X + inset + position.Value * (bounds.Width - thumbWidth - 2 * inset);
        using SolidBrush thumb = new(thumbColor);
        using GraphicsPath knob = CaptureDrawing.Rounded(new RectangleF(x, bounds.Y + (bounds.Height - diameter) / 2, thumbWidth, diameter),
            p.CornerRadius(diameter / 2));
        g.FillPath(thumb, knob);
        bool focused = Focused && ShowFocusCues;
        if (p.HasShapedControls)
        {
            Color edge = !Enabled ? p.Border : CaptureDrawing.Blend(p.FieldBorder, p.Accent, position.Value);
            if (focused) edge = position.Value < .5f ? p.Accent : p.AccentText;
            CaptureDrawing.DrawInsideBorder(g, bounds, radius, edge, (focused ? 1.5f : p.IsMaterial ? 1.5f : 1) * s);
        }
        else if (focused)
        {
            RectangleF ringBounds = bounds;
            ringBounds.Inflate(1.5f * s, 1.5f * s);
            using GraphicsPath ring = CaptureDrawing.Rounded(ringBounds, 13.5f * s);
            using Pen focus = new(p.Accent, 1.5f * s);
            g.DrawPath(focus, ring);
        }
    }
}
