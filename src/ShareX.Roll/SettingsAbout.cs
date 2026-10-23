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
            ColumnCount = 1, Padding = new Padding(24, 26, 24, 18), Margin = new Padding(0, 0, 0, 8)
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
        TableLayoutPanel branding = new() { Name = "aboutBranding", AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        branding.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        branding.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        branding.Controls.Add(new AppMark
        {
            Name = "aboutAppMark", Size = new Size(56, 56), Margin = new Padding(0, 0, 16, 0),
            Anchor = AnchorStyles.Left, TabStop = false, AccessibleName = "SnapFlow", AccessibleRole = AccessibleRole.Graphic
        }, 0, 0);
        TableLayoutPanel identity = new() { AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int i = 0; i < 2; i++) identity.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        identity.Controls.Add(new Label
        {
            Name = "aboutAppName", Text = "SnapFlow", AutoSize = true, Font = new Font(font.FontFamily, 20, FontStyle.Bold), Margin = Padding.Empty
        }, 0, 0);
        identity.Controls.Add(new Label
        {
            Name = "aboutVersion", Text = "Version " + version, AutoSize = true, Font = new Font(font.FontFamily, 10), Tag = "secondary", Margin = new Padding(0, 4, 0, 0)
        }, 0, 1);
        branding.Controls.Add(identity, 1, 0);
        Add(Center(branding, new Padding(0, 0, 0, 12)));
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
                Name = name, Dock = DockStyle.Fill, Height = 78, Font = new Font(font.FontFamily, 10),
                AccessibleName = heading + ": " + detail, AccessibleDescription = target
            };
            button.Click += (_, _) => Open(target, heading);
            return button;
        }
        AboutLinkButton repository = Link("aboutRepository", L("AboutRepository"), "https://github.com/shrnbshr/SnapFlow", "https://github.com/shrnbshr/SnapFlow", false);
        AboutLinkButton account = Link("aboutGitHubAccount", L("AboutGitHubAccount"), "@shrnbshr", "https://github.com/shrnbshr", true);
        TableLayoutPanel project = new() { Name = "aboutProject", Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 20) };
        project.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        project.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        project.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        project.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        project.Controls.Add(repository, 0, 0);
        project.Controls.Add(account, 1, 0);
        bool? stacked = null;
        bool queued = false;
        void ArrangeLinks()
        {
            int gap = (int)Math.Round(10 * project.DeviceDpi / 96f);
            int minimum = Math.Max(repository.DetailWidth, account.DetailWidth) + (int)(78 * project.DeviceDpi / 96f);
            bool narrow = project.ClientSize.Width < minimum * 2 + gap;
            if (stacked == narrow) return;
            stacked = narrow;
            project.SuspendLayout();
            project.SetColumnSpan(repository, narrow ? 2 : 1);
            project.SetCellPosition(account, new TableLayoutPanelCellPosition(narrow ? 0 : 1, narrow ? 1 : 0));
            project.SetColumnSpan(account, narrow ? 2 : 1);
            repository.Margin = narrow ? new Padding(0, 0, 0, gap) : new Padding(0, 0, gap / 2, 0);
            account.Margin = narrow ? Padding.Empty : new Padding(gap - gap / 2, 0, 0, 0);
            project.ResumeLayout(true);
            about.PerformLayout();
            about.Parent?.PerformLayout(about, nameof(Control.PreferredSize));
        }
        void QueueLayout()
        {
            if (queued || !project.IsHandleCreated || project.IsDisposed) return;
            queued = true;
            // Reflow after the current parent measurement, so AutoScroll includes the new row.
            project.BeginInvoke((Action)(() =>
            {
                queued = false;
                if (!project.IsDisposed) ArrangeLinks();
            }));
        }
        project.HandleCreated += (_, _) => QueueLayout();
        project.SizeChanged += (_, _) => QueueLayout();
        project.DpiChangedAfterParent += (_, _) => { stacked = null; QueueLayout(); };
        repository.FontChanged += (_, _) => { stacked = null; QueueLayout(); };
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
                MinimumSize = new Size(92, 34), Padding = new Padding(10, 0, 10, 0), Font = new Font(font.FontFamily, 10),
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
    private CapturePalette palette = CapturePalette.Light;
    internal int DetailWidth => TextRenderer.MeasureText(detail, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
    internal int DetailAvailable => Width - (int)(78 * DeviceDpi / 96f);

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
    }
    internal void ApplyPalette(CapturePalette value) { palette = value; Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { hover.SpringTo(1); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover.SpringTo(0); press.SpringTo(0); base.OnMouseLeave(e); }
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
        using GraphicsPath shape = CaptureDrawing.Rounded(new RectangleF(s, s, Width - 2 * s, Height - 2 * s), 12 * s);
        using SolidBrush fill = new(CaptureDrawing.Blend(CaptureDrawing.Blend(palette.Canvas, palette.Hover, hover.Value), palette.Tint, press.Value));
        using Pen edge = new(Focused && ShowFocusCues ? palette.Accent : palette.Border, (Focused && ShowFocusCues ? 1.75f : 1) * s);
        g.FillPath(fill, shape);
        g.DrawPath(edge, shape);
        using Pen glyph = new(palette.Secondary, 1.5f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float x = 18 * s, y = Height / 2f - 10 * s;
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
        TextRenderer.DrawText(g, heading, title, new Rectangle((int)(48 * s), (int)(16 * s), Math.Max(1, DetailAvailable), (int)(24 * s)), palette.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, detail, Font, new Rectangle((int)(48 * s), (int)(42 * s), Math.Max(1, DetailAvailable), (int)(22 * s)), palette.Secondary,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        float arrowX = Width - 22 * s, arrowY = Height / 2f;
        g.DrawLine(glyph, arrowX - 4 * s, arrowY + 4 * s, arrowX + 3 * s, arrowY - 3 * s);
        g.DrawLines(glyph, [new PointF(arrowX - 3 * s, arrowY - 3 * s), new PointF(arrowX + 3 * s, arrowY - 3 * s), new PointF(arrowX + 3 * s, arrowY + 3 * s)]);
    }
}
