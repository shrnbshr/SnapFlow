// GPL-3.0-or-later. About presentation; links use the operating system's default handlers.
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using static ShareX.Roll.Localization.UiText;

namespace ShareX.Roll;

internal static class SettingsAbout
{
    internal static SettingsGroup Create(Font font, Action<string> feedback)
    {
        SettingsGroup about = new()
        {
            Name = "aboutSoftware", DrawDividers = false, Dock = DockStyle.Top, AutoSize = true,
            ColumnCount = 1, Padding = new Padding(20, 20, 20, 12), Margin = new Padding(0, 0, 0, 8)
        };
        about.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control control)
        {
            int row = about.RowCount++;
            about.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            about.Controls.Add(control, 0, row);
        }
        TableLayoutPanel Center(Control child, Padding margin)
        {
            TableLayoutPanel holder = new() { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 3, RowCount = 1, Margin = margin };
            holder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            holder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            holder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            holder.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            holder.Controls.Add(child, 1, 0);
            return holder;
        }
        string version = typeof(SettingsAbout).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        TableLayoutPanel branding = new()
        {
            Name = "aboutBranding", AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2,
            Margin = new Padding(0, 0, 0, 12)
        };
        branding.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 2; i++) branding.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        branding.Controls.Add(new Label
        {
            Name = "aboutAppName", Text = "SnapFlow", AutoSize = true, Anchor = AnchorStyles.None,
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font(font.FontFamily, 20, FontStyle.Bold), Margin = Padding.Empty
        }, 0, 0);
        branding.Controls.Add(new Label
        {
            Name = "aboutVersion", Text = "Version " + version, AutoSize = true, Anchor = AnchorStyles.None,
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font(font.FontFamily, 10), Tag = "secondary", Margin = new Padding(0, 4, 0, 0)
        }, 0, 1);
        Add(branding);
        Add(new Label
        {
            Name = "aboutTagline", Text = L("AboutTagline"), AutoSize = true, Dock = DockStyle.Top,
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font(font.FontFamily, 10.5f),
            Tag = "secondary", Margin = new Padding(0, 0, 0, 24)
        });

        void Open(string target, string caption)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
            { feedback(LF("OpenReferenceFailed", caption)); }
        }
        AboutLinkButton Link(string name, string heading, string detail, string target, bool profile)
        {
            AboutLinkButton button = new(heading, detail, profile)
            {
                Name = name, Dock = DockStyle.Fill, Height = 88, Font = new Font(font.FontFamily, 10),
                AccessibleName = heading + ": " + detail, AccessibleDescription = target
            };
            button.Click += (_, _) => Open(target, heading);
            return button;
        }
        AboutLinkButton repository = Link("aboutRepository", L("AboutRepository"), "shrnbshr/SnapFlow", "https://github.com/shrnbshr/SnapFlow", false);
        AboutLinkButton account = Link("aboutGitHubAccount", L("AboutGitHubAccount"), "@shrnbshr", "https://github.com/shrnbshr", true);
        TableLayoutPanel project = new() { Name = "aboutProject", Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 20) };
        project.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        project.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        project.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        repository.Margin = new Padding(0, 0, SettingsLayout.Gap / 2, 0);
        account.Margin = new Padding(SettingsLayout.Gap / 2, 0, 0, 0);
        project.Controls.Add(repository, 0, 0);
        project.Controls.Add(account, 1, 0);
        ToolTip projectTips = new();
        projectTips.SetToolTip(repository, repository.AccessibleDescription);
        projectTips.SetToolTip(account, account.AccessibleDescription);
        about.Disposed += (_, _) => projectTips.Dispose();
        Add(project);
        Add(new Label
        {
            Name = "aboutOrigin", Text = L("AboutOrigin"), AutoSize = true, Dock = DockStyle.Top,
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font(font.FontFamily, 10), Tag = "secondary", Margin = new Padding(8, 0, 8, 8)
        });
        Add(new Label
        {
            Name = "aboutCredits", Text = L("AboutLicense") + Environment.NewLine + "Copyright (c) 2007–2026 ShareX Team.",
            AutoSize = true, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(font.FontFamily, 10), Tag = "secondary", Margin = new Padding(0, 0, 0, 12)
        });
        FlowLayoutPanel links = new() { Name = "aboutLinks", AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        void Document(string name, string text, string target)
        {
            Button button = new()
            {
                Name = name, Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(112, ButtonSpacing.ActionHeight), Padding = ButtonSpacing.ActionPadding, Font = new Font(font.FontFamily, 10),
                Tag = "quiet", Margin = new Padding(2, 0, 2, 0), FlatStyle = FlatStyle.Flat, AccessibleDescription = target
            };
            button.Click += (_, _) => Open(target, text);
            links.Controls.Add(button);
        }
        Document("aboutUpstream", L("AboutUpstream"), "https://github.com/ShareX/ShareX/tree/v21.0.0");
        Document("aboutLicense", L("AboutLicenseLink"), Path.Combine(AppContext.BaseDirectory, "LICENSE.txt"));
        Document("aboutNotice", L("AboutNoticeLink"), Path.Combine(AppContext.BaseDirectory, "NOTICE.md"));
        Add(Center(links, Padding.Empty));
        return about;
    }
}

internal sealed class AboutLinkButton : Button
{
    private readonly string heading, detail;
    private readonly bool profile;
    private readonly UiTween hover, press;
    private readonly ButtonElevation elevation = new();
    private CapturePalette palette = CapturePalette.Light;
    internal int DetailWidth => TextRenderer.MeasureText(detail, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
    internal int DetailAvailable => Width - (int)(100 * DeviceDpi / 96f);

    internal AboutLinkButton(string heading, string detail, bool profile)
    {
        this.heading = heading;
        this.detail = detail;
        this.profile = profile;
        Text = heading + Environment.NewLine + detail;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        hover = new(this, Invalidate);
        press = new(this, Invalidate);
        Disposed += (_, _) => elevation.Dispose();
    }
    internal void ApplyPalette(CapturePalette value) { palette = value; Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { hover.SpringTo(1, ButtonElevation.ResponseMilliseconds); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover.SpringTo(0, ButtonElevation.ResponseMilliseconds); press.SpringTo(0); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) press.SnapTo(1); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { press.SpringTo(0, 110); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) press.SnapTo(1); base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { press.SpringTo(0, 110); base.OnKeyUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { press.SpringTo(0); Invalidate(); base.OnLostFocus(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) press.SpringTo(0); base.OnMouseCaptureChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        float s = DeviceDpi / 96f;
        g.Clear(Parent?.BackColor ?? palette.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF face = ButtonElevation.CompactFaceBounds(ClientSize, s);
        elevation.PaintShadow(g, ClientSize, s, palette.Background.GetBrightness() < .5f, Enabled ? hover.Value * (1 - press.Value) : 0, compact: true, unified: true);
        using GraphicsPath shape = CaptureDrawing.Rounded(face, ButtonElevation.CompactRadius(face, s));
        using SolidBrush fill = new(CaptureDrawing.Blend(CaptureDrawing.Blend(palette.Canvas, ButtonElevation.Surface(palette), hover.Value), palette.Tint, press.Value));
        g.FillPath(fill, shape);
        if (Focused && ShowFocusCues || SystemInformation.HighContrast)
        {
            using Pen edge = new(Focused ? palette.Accent : palette.Border, 1.75f * s);
            g.DrawPath(edge, shape);
        }
        using Pen glyph = new(palette.Secondary, 1.5f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float x = face.Left + ButtonSpacing.Horizontal * s, y = Height / 2f - 10 * s;
        if (profile)
        {
            g.DrawEllipse(glyph, x + 6 * s, y, 8 * s, 8 * s);
            g.DrawArc(glyph, x + 2 * s, y + 12 * s, 16 * s, 14 * s, 180, 180);
        }
        else
        {
            g.DrawRectangle(glyph, x + 3 * s, y, 15 * s, 20 * s);
            g.DrawLine(glyph, x + 7 * s, y, x + 7 * s, y + 20 * s);
            g.DrawLine(glyph, x + 10 * s, y + 5 * s, x + 15 * s, y + 5 * s);
        }
        using Font title = new(Font.FontFamily, 10.5f, FontStyle.Bold);
        int titleHeight = TextRenderer.MeasureText("Ag", title).Height;
        int detailHeight = TextRenderer.MeasureText("Ag", Font).Height;
        int top = (Height - titleHeight - detailHeight - (int)(6 * s)) / 2;
        int left = (int)(face.Left + (ButtonSpacing.Horizontal + 20 + ButtonSpacing.IconGap) * s);
        TextRenderer.DrawText(g, heading, title, new Rectangle(left, top, Math.Max(1, DetailAvailable), titleHeight), palette.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, detail, Font, new Rectangle(left, top + titleHeight + (int)(6 * s), Math.Max(1, DetailAvailable), detailHeight), palette.Secondary,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        float arrowX = face.Right - ButtonSpacing.Horizontal * s, arrowY = Height / 2f;
        g.DrawLine(glyph, arrowX - 4 * s, arrowY + 4 * s, arrowX + 3 * s, arrowY - 3 * s);
        g.DrawLines(glyph, [new PointF(arrowX - 3 * s, arrowY - 3 * s), new PointF(arrowX + 3 * s, arrowY - 3 * s), new PointF(arrowX + 3 * s, arrowY + 3 * s)]);
    }
}
