// GPL-3.0-or-later. Native checkbox semantics with a short switch animation.
using System.Drawing.Drawing2D;

namespace ShareX.Roll;

internal sealed class MotionToggle : CheckBox
{
    private readonly UiTween position;
    private readonly UiTween hover, press;
    internal CapturePalette Palette { get; set; } = CapturePalette.Light;

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
        RectangleF bounds = new(2 * s, (Height - 24 * s) / 2, Width - 4 * s, 24 * s);
        using GraphicsPath track = CaptureDrawing.Rounded(bounds, 12 * s);
        Color off = CaptureDrawing.Blend(p.Secondary, p.Surface, .12f);
        Color trackColor = CaptureDrawing.Blend(off, p.Accent, position.Value);
        if (!Enabled) trackColor = CaptureDrawing.Blend(trackColor, p.Surface, .45f);
        using SolidBrush fill = new(CaptureDrawing.Blend(trackColor, p.Text, hover.Value * .06f));
        g.FillPath(fill, track);
        float diameter = 18 * s;
        float thumbWidth = diameter + 3 * s * press.Value;
        float x = bounds.X + 3 * s + position.Value * (bounds.Width - thumbWidth - 6 * s);
        using SolidBrush thumb = new(CaptureDrawing.Blend(p.Surface, p.AccentText, position.Value));
        using GraphicsPath knob = CaptureDrawing.Rounded(new RectangleF(x, bounds.Y + 3 * s, thumbWidth, diameter), diameter / 2);
        g.FillPath(thumb, knob);
        if (Focused && ShowFocusCues)
        {
            RectangleF ringBounds = bounds;
            ringBounds.Inflate(1.5f * s, 1.5f * s);
            using GraphicsPath ring = CaptureDrawing.Rounded(ringBounds, 13.5f * s);
            using Pen focus = new(p.Accent, 1.5f * s);
            g.DrawPath(focus, ring);
        }
    }
}
