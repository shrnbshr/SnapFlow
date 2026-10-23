using static ShareX.Roll.Localization.UiText;
// GPL-3.0-or-later. Workspace composition only; original capture handlers stay attached.
using ShareX.HelpersLib;
using System.Drawing.Drawing2D;

namespace ShareX.Roll;

public sealed partial class MainForm
{
    private readonly Label captureTip = new()
    {
        Text = L("CaptureTip"), AutoSize = true, Dock = DockStyle.Bottom,
        Tag = "secondary", Margin = new Padding(4, 8, 0, 0)
    };
    private TableLayoutPanel captureSidebar = null!;

    private void InitializeWorkspaceLayout()
    {
        workspace = new TableLayoutPanel
        {
            Name = "workspace", Dock = DockStyle.Fill, Padding = new Padding(28, 20, 28, 16),
            ColumnCount = 1, RowCount = 3, Margin = Padding.Empty
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        TableLayoutPanel heading = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 18) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        heading.Controls.Add(new Label
        {
            Name = "workspaceName", Text = "SnapFlow", AutoSize = true, Font = new Font("Georgia", 18, FontStyle.Bold),
            Margin = new Padding(0, 1, 0, 0)
        }, 0, 0);
        settingsButton.AutoSize = false;
        settingsButton.MinimumSize = Size.Empty;
        settingsButton.Size = new Size(104, 36);
        settingsButton.Text = L("Settings");
        settingsButton.Tag = "quiet";
        settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        settingsButton.Margin = new Padding(0, 2, 0, 0);
        settingsButton.AccessibleDescription = L("SettingsDescription");
        settingsButton.Click += (_, _) => OpenSettings();
        heading.Controls.Add(settingsButton, 1, 0);
        workspace.Controls.Add(heading, 0, 0);

        TableLayoutPanel body = new() { Name = "captureBody", Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        captureSidebar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = ApplicationPreferences.Actions.Length + 3, Margin = new Padding(0, 0, 24, 0) };
        captureSidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        captureSidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        captureSidebar.Controls.Add(new Label
        {
            Name = "captureHeading", Text = L("NewCapture"), AutoSize = true, Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(4, 0, 0, 0), Anchor = AnchorStyles.Left
        }, 0, 0);
        int row = 1;
        foreach (CaptureShortcut action in ApplicationPreferences.Actions)
        {
            captureSidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            CaptureModeButton button = new()
            {
                Name = action.Kind + "Button", Text = action.Title, Kind = action.Kind,
                Shortcut = new HotkeyInfo(preferences.Shortcuts[action.Kind]).ToString(),
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), AccessibleName = action.Title, TabIndex = row - 1
            };
            button.Click += async (_, _) => await CaptureAsync(action.Kind);
            modeButtons.Add(action.ModeKey, button);
            captureSidebar.Controls.Add(button, 0, row++);
        }
        captureSidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        captureSidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        captureTip.Font = new Font(Font.FontFamily, 10);
        captureSidebar.Controls.Add(captureTip, 0, row + 1);
        captureSidebar.SizeChanged += (_, _) => captureTip.Visible = captureSidebar.ClientSize.Height >= 430 * DeviceDpi / 96f;
        body.Controls.Add(captureSidebar, 0, 0);

        previewFrame = new PreviewSurface
        {
            Name = "previewSurface", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Padding = new Padding(16), Margin = Padding.Empty
        };
        previewFrame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        previewFrame.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewFrame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        TableLayoutPanel toolbar = new() { Name = "previewToolbar", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 0, 0, 10), Margin = Padding.Empty };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        FlowLayoutPanel caption = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        previewTitle.Text = L("Preview");
        previewTitle.Font = new Font(Font, FontStyle.Bold);
        previewTitle.Margin = Padding.Empty;
        previewHint.Text = L("Waiting");
        previewHint.Font = new Font(Font.FontFamily, 10);
        previewHint.Tag = "secondary";
        previewHint.Margin = new Padding(0, 2, 0, 0);
        caption.Controls.AddRange([previewTitle, previewHint]);
        toolbar.Controls.Add(caption, 0, 0);
        FlowLayoutPanel actions = new() { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = Padding.Empty };
        foreach (Button button in new[] { edit, copy, save })
        {
            button.AutoSize = false;
            button.MinimumSize = Size.Empty;
            button.Size = new Size(82, 34);
            button.Padding = Padding.Empty;
            button.Margin = new Padding(6, 0, 0, 0);
            button.Font = new Font(Font.FontFamily, 10);
            button.Tag = "quiet";
            button.TabIndex = actions.Controls.Count;
            actions.Controls.Add(button);
        }
        copy.Text = L("Copy");
        save.Text = L("Save");
        toolbar.Controls.Add(actions, 1, 0);
        bool reflowPending = false;
        void ReflowToolbar()
        {
            bool narrow = toolbar.ClientSize.Width < 560 * DeviceDpi / 96f;
            if (toolbar.GetRow(actions) == (narrow ? 1 : 0) && toolbar.GetColumnSpan(caption) == (narrow ? 2 : 1)) return;
            toolbar.SuspendLayout();
            try
            {
                toolbar.SetColumnSpan(caption, narrow ? 2 : 1);
                toolbar.SetCellPosition(actions, new TableLayoutPanelCellPosition(narrow ? 0 : 1, narrow ? 1 : 0));
                toolbar.SetColumnSpan(actions, narrow ? 2 : 1);
            }
            finally
            {
                toolbar.ResumeLayout(true);
                previewFrame.PerformLayout();
            }
        }
        void QueueToolbarLayout()
        {
            if (reflowPending || !toolbar.IsHandleCreated || toolbar.IsDisposed) return;
            reflowPending = true;
            // Reparent cells after the current table-layout pass has finished.
            toolbar.BeginInvoke((Action)(() =>
            {
                reflowPending = false;
                if (!toolbar.IsDisposed) ReflowToolbar();
            }));
        }
        toolbar.HandleCreated += (_, _) => QueueToolbarLayout();
        toolbar.SizeChanged += (_, _) => QueueToolbarLayout();
        previewHint.TextChanged += (_, _) => QueueToolbarLayout();
        edit.TextChanged += (_, _) => QueueToolbarLayout();
        previewFrame.Controls.Add(toolbar, 0, 0);
        preview.Margin = Padding.Empty;
        previewFrame.Controls.Add(preview, 0, 1);
        body.Controls.Add(previewFrame, 1, 0);
        workspace.Controls.Add(body, 0, 1);
        status.Tag = "secondary";
        status.Font = new Font(Font.FontFamily, 10);
        status.Margin = Padding.Empty;
        status.AccessibleName = L("CaptureStatus");
        workspace.Controls.Add(status, 0, 2);
        Controls.Add(workspace);
    }

    private void RefreshPreviewToolbar()
    {
        previewHint.Text = preview.Image is { } image ? LF("Pixels", image.Width, image.Height) : L("Waiting");
        appearancePalette.StyleButton(copy);
        appearancePalette.StyleButton(save, true);
        appearancePalette.StyleButton(edit);
    }

    private void ApplyWorkspaceLanguage()
    {
        SuspendLayout();
        settingsButton.Text = L("Settings");
        settingsButton.AccessibleDescription = L("SettingsDescription");
        captureTip.Text = L("CaptureTip");
        Controls.Find("captureHeading", true).Single().Text = L("NewCapture");
        foreach (CaptureShortcut action in ApplicationPreferences.Actions)
        {
            CaptureModeButton button = modeButtons[action.ModeKey];
            button.Text = action.Title;
            button.AccessibleName = action.Title;
        }
        previewTitle.Text = L("Preview");
        copy.Text = L("Copy");
        save.Text = L("Save");
        edit.Text = L("Edit");
        status.AccessibleName = L("CaptureStatus");
        RefreshPreviewToolbar();
        preview.ApplyViewportLanguage();
        preview.Invalidate();
        ResumeLayout(true);
    }
}

internal sealed class AppMark : Control
{
    internal CapturePalette Palette { get; set; } = CapturePalette.Light;
    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = CaptureDrawing.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 10 * s);
        using SolidBrush fill = new(Palette.Accent);
        g.FillPath(fill, path);
        CaptureDrawing.Icon(g, "region", new RectangleF(6 * s, 6 * s, Width - 12 * s, Height - 12 * s), Palette.AccentText);
    }
}

internal sealed class PreviewSurface : TableLayoutPanel
{
    internal CapturePalette Palette { get; set; } = CapturePalette.Light;
    internal PreviewSurface() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Palette.Background);
        using GraphicsPath path = CaptureDrawing.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 20 * DeviceDpi / 96f);
        using SolidBrush fill = new(Palette.Surface);
        using Pen border = new(Palette.Border);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
    }
}
