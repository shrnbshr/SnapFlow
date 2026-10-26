// GPL-3.0-or-later. Reuse the same scrolling fields and appearance as the Settings page.
using ShareX.ScreenCaptureLib;
using ShareX.HelpersLib;
using static ShareX.Roll.Localization.UiText;

namespace ShareX.Roll;

internal sealed class ScrollingOptionsDialog : Form
{
    internal ScrollingOptionsDialog(ScrollingCaptureOptions options, CapturePalette palette, bool transparent)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 11);
        Text = L("ScrollTitle");
        Icon = ShareXResources.Icon;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(600, 650);
        MinimumSize = new Size(520, 520);
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(28), Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = L("Scroll"), AutoSize = true, Font = new Font(Font.FontFamily, 18, FontStyle.Bold), Margin = new Padding(0, 0, 0, SettingsLayout.SectionGap) }, 0, 0);
        ScrollingOptionsEditor editor = new(options);
        SettingsGroup group = new()
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 1,
            Padding = new Padding(SettingsLayout.Inset, SettingsLayout.Vertical, SettingsLayout.Inset, 0), Margin = Padding.Empty
        };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        group.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        group.Controls.Add(editor, 0, 0);
        Panel viewport = new SettingsScrollViewport { AutoScroll = true, Margin = Padding.Empty };
        viewport.Controls.Add(group);
        SettingsScrollHost host = new(viewport) { Dock = DockStyle.Fill, Margin = Padding.Empty };
        root.Controls.Add(host, 0, 1);
        Button cancel = new() { Name = "cancelScrollOptions", Text = L("Cancel"), AutoSize = true, Padding = ButtonSpacing.ActionPadding, Tag = "quiet", DialogResult = DialogResult.Cancel };
        Button save = new() { Name = "saveScrollOptions", Text = L("SaveReturn"), AutoSize = true, Padding = ButtonSpacing.ActionPadding };
        FlowLayoutPanel footer = new() { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, SettingsLayout.Gap, 0, 0) };
        save.Margin = Padding.Empty;
        cancel.Margin = new Padding(0, 0, SettingsLayout.Gap, 0);
        footer.Controls.AddRange([save, cancel]);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
        save.Click += (_, _) =>
        {
            ScrollingCaptureOptions updated = editor.ReadOptions();
            options.StartDelay = updated.StartDelay;
            options.ScrollDelay = updated.ScrollDelay;
            options.ScrollMethod = updated.ScrollMethod;
            options.ScrollAmount = updated.ScrollAmount;
            options.AutoScrollTop = updated.AutoScrollTop;
            options.ShowRegion = updated.ShowRegion;
            options.AutoIgnoreBottomEdge = updated.AutoIgnoreBottomEdge;
            options.AutoUpload = updated.AutoUpload;
            DialogResult = DialogResult.OK;
            Close();
        };
        AcceptButton = save;
        CancelButton = cancel;
        SettingsColors.Apply(this, palette, save, () => transparent);
        host.ApplyPalette(palette);
        UiLanguage.ApplyTextDirection(this);
        WindowBackdrop backdrop = new(this);
        HandleCreated += (_, _) => backdrop.Apply(palette, transparent);
        AutoScaleDimensions = new SizeF(96, 96);
        ResumeLayout(true);
    }
}
