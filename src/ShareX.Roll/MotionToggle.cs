// GPL-3.0-or-later. Native checkbox semantics with a short switch animation.
using System.Drawing.Drawing2D;

namespace ShareX.Roll;

internal sealed class MotionToggle : CheckBox
{
    private readonly UiTween position;
    internal CapturePalette Palette { get; set; } = CapturePalette.Light;

    internal MotionToggle()
    {
        AutoSize = false;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        position = new(this, Invalidate);
    }

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
        using SolidBrush fill = new(CaptureDrawing.Blend(p.Border, p.Accent, position.Value));
        g.FillPath(fill, track);
        float diameter = 18 * s;
        float x = bounds.X + 3 * s + position.Value * (bounds.Width - diameter - 6 * s);
        using SolidBrush thumb = new(CaptureDrawing.Blend(p.Surface, p.AccentText, position.Value));
        g.FillEllipse(thumb, x, bounds.Y + 3 * s, diameter, diameter);
        if (Focused && ShowFocusCues)
        {
            using Pen focus = new(p.Accent, 2 * s);
            g.DrawPath(focus, track);
        }
    }
}
