// GPL-3.0-or-later. Rounded folder presentation around a native, selectable read-only text field.
using System.Drawing.Drawing2D;
using static ShareX.Roll.Localization.UiText;

namespace ShareX.Roll;

internal sealed class SettingsFolderField : Control
{
    private readonly TextBox text;
    private readonly ToolTip tooltip = new() { ShowAlways = true };
    private readonly UiTween focus;
    private CapturePalette palette = CapturePalette.Light;

    internal SettingsFolderField(TextBox text)
    {
        this.text = text;
        Name = "defaultSaveFolderField";
        Height = 38;
        TabStop = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        focus = new(this, Invalidate);
        text.BorderStyle = BorderStyle.None;
        text.Dock = DockStyle.None;
        text.Margin = Padding.Empty;
        text.AutoSize = true;
        text.TextChanged += (_, _) => UpdatePath();
        text.HandleCreated += (_, _) => UpdatePath();
        text.VisibleChanged += (_, _) => { if (text.Visible) UpdatePath(); };
        text.GotFocus += (_, _) => focus.AnimateTo(1, 100);
        text.LostFocus += (_, _) => focus.AnimateTo(0, 100);
        Controls.Add(text);
        UpdatePath();
    }

    internal void ApplyPalette(CapturePalette value)
    {
        palette = value;
        BackColor = Parent?.BackColor ?? value.Surface;
        text.BackColor = value.Canvas;
        text.ForeColor = value.Text;
        Invalidate();
    }

    private void UpdatePath()
    {
        bool hasPath = !string.IsNullOrEmpty(text.Text);
        text.Visible = hasPath;
        AccessibleName = text.AccessibleName;
        AccessibleDescription = hasPath ? text.Text : L("NoSaveFolderSelected");
        tooltip.SetToolTip(this, text.Text);
        tooltip.SetToolTip(text, text.Text);
        // Preserve the useful tail of a long path. Home/End, selection and Copy stay native.
        text.SelectionStart = text.TextLength;
        text.SelectionLength = 0;
        text.ScrollToCaret();
        Invalidate();
    }

    // Empty native text fields otherwise leave Control's default 100px preferred height in the table.
    public override Size GetPreferredSize(Size proposedSize) => new(0,
        Math.Max((int)(38 * DeviceDpi / 96f), text.PreferredHeight + (int)(12 * DeviceDpi / 96f)));

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (text == null) return;
        float scale = DeviceDpi / 96f;
        text.SetBounds((int)(36 * scale), Math.Max(0, (Height - text.PreferredHeight) / 2),
            Math.Max(1, Width - (int)(48 * scale)), text.PreferredHeight);
        UpdatePath();
    }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) text.Focus(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        float scale = DeviceDpi / 96f;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(BackColor);
        RectangleF bounds = new(scale, scale, Width - 2 * scale, Height - 2 * scale);
        using GraphicsPath shape = CaptureDrawing.Rounded(bounds, 10 * scale);
        using SolidBrush fill = new(palette.Canvas);
        using Pen edge = new(CaptureDrawing.Blend(palette.Border, palette.Accent, focus.Value), (ContainsFocus ? 1.5f : 1) * scale);
        graphics.FillPath(fill, shape);
        graphics.DrawPath(edge, shape);
        // A small folder outline makes the location field recognizable beside numeric and language inputs.
        float x = 12 * scale, y = (Height - 14 * scale) / 2;
        using GraphicsPath icon = new();
        icon.AddLines([new PointF(x, y + 2 * scale), new(x + 5 * scale, y + 2 * scale),
            new(x + 7 * scale, y + 4 * scale), new(x + 16 * scale, y + 4 * scale),
            new(x + 16 * scale, y + 14 * scale), new(x, y + 14 * scale)]);
        icon.CloseFigure();
        using Pen ink = new(palette.Secondary, 1.4f * scale) { LineJoin = LineJoin.Round };
        graphics.DrawPath(ink, icon);
        if (string.IsNullOrEmpty(text.Text))
        {
            Rectangle caption = new((int)(36 * scale), 0, Math.Max(0, Width-(int)(48 * scale)), Height);
            TextRenderer.DrawText(graphics, L("NoSaveFolderSelected"), text.Font, caption, palette.Secondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
        base.OnPaint(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tooltip.Dispose();
        base.Dispose(disposing);
    }
}
