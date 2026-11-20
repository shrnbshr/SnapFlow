// GPL-3.0-or-later. Immediate, non-interactive hints for the workspace commands.
using System.Drawing.Drawing2D;

namespace ShareX.Roll;

internal sealed class WorkspaceToolTip : Control
{
    private const float CornerRadius = 8;
    private const TextFormatFlags CaptionFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
    private readonly Control viewport;
    private readonly Func<CapturePalette> palette;
    private CaptureModeButton? current;
    private float scale = 1;

    internal WorkspaceToolTip(Control viewport, Func<CapturePalette> palette)
    {
        this.viewport = viewport;
        this.palette = palette;
        Name = "workspaceToolTip";
        Visible = TabStop = false;
        AccessibleRole = AccessibleRole.ToolTip;
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    internal void Track(CaptureModeButton command)
    {
        command.MouseEnter += (_, _) => ShowHint(command);
        command.MouseLeave += (_, _) => HideFor(command);
        command.MouseDown += (_, _) => HideFor(command);
        command.VisibleChanged += (_, _) => { if (!command.Visible) HideFor(command); };
        command.EnabledChanged += (_, _) => { if (!command.Enabled) HideFor(command); };
    }

    internal static string GetCaption(CaptureModeButton command) =>
        command.Shortcut.Length == 0 ? command.Text : $"{command.Text} ({command.Shortcut})";

    private void ShowHint(CaptureModeButton command)
    {
        if (!command.Visible || !command.Enabled || !viewport.Visible) return;

        current = command;
        Text = GetCaption(command);
        scale = command.DeviceDpi / 96f;
        using Graphics graphics = viewport.CreateGraphics();
        Size caption = TextRenderer.MeasureText(graphics, Text, Font, Size.Empty, CaptionFlags);
        Point center = viewport.PointToClient(command.PointToScreen(new Point(command.Width / 2, command.Height)));
        int margin = D(6);
        int width = Math.Min(caption.Width + D(20), viewport.ClientSize.Width - 2 * margin);
        int height = caption.Height + D(12);
        if (width <= 0 || height + 2 * margin > viewport.ClientSize.Height) { HideHint(); return; }

        int left = Math.Clamp(center.X - width / 2, margin, viewport.ClientSize.Width - width - margin);
        Bounds = new Rectangle(left, margin, width, height);
        using GraphicsPath outline = CaptureDrawing.Rounded(ClientRectangle, CornerRadius * scale);
        Region? previous = Region;
        Region = new Region(outline);
        previous?.Dispose();
        BringToFront();
        Show();
        Invalidate();
    }

    private int D(int value) => (int)Math.Round(value * scale);

    private void HideFor(CaptureModeButton command)
    {
        if (current == command) HideHint();
    }

    internal void HideHint()
    {
        current = null;
        Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        CapturePalette colors = palette();
        e.Graphics.Clear(colors.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        CaptureDrawing.DrawInsideBorder(e.Graphics, ClientRectangle, CornerRadius * scale, colors.Border, scale);
        Rectangle caption = ClientRectangle;
        caption.Inflate(-D(10), 0);
        TextRenderer.DrawText(e.Graphics, Text, Font, caption, colors.Text,
            CaptionFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | UiLanguage.TextDirection);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0084) // WM_NCHITTEST
        {
            message.Result = -1; // HTTRANSPARENT
            return;
        }
        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        Region? shape = disposing ? Region : null;
        base.Dispose(disposing);
        shape?.Dispose();
    }
}
