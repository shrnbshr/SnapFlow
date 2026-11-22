// GPL-3.0-or-later. Workspace composition only; original capture handlers stay attached.
using static ShareX.Roll.Localization.UiText;
using System.Drawing.Drawing2D;

namespace ShareX.Roll;

public sealed partial class MainForm
{
    private WorkspaceToolTip workspaceTips = null!;
    private FlowLayoutPanel previewActions = null!;

    private sealed class BufferedCommandGroup : FlowLayoutPanel
    {
        internal BufferedCommandGroup()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    private void InitializeWorkspaceLayout()
    {
        workspace = new TableLayoutPanel
        {
            Name = "workspace", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        TableLayoutPanel toolbar = new()
        {
            Name = "previewToolbar", Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(12, 6, 12, 6)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        FlowLayoutPanel captureCommands = CommandGroup("captureCommands");
        foreach (CaptureShortcut action in ApplicationPreferences.Actions)
        {
            CaptureModeButton button = new() { Name = action.Kind + "Button", Kind = action.Kind };
            button.Click += async (_, _) => await CaptureAsync(action.Kind);
            modeButtons.Add(action.ModeKey, button);
            captureCommands.Controls.Add(button);
            SetCommandLabel(button, action.Title);
        }
        toolbar.Controls.Add(captureCommands, 0, 0);
        previewActions = CommandGroup("previewActions");
        previewActions.Margin = new Padding(0, 0, 10, 0);
        previewActions.Controls.AddRange([edit, copy, save]);
        toolbar.Controls.Add(previewActions, 2, 0);
        settingsButton.Margin = Padding.Empty;
        settingsButton.Click += (_, _) => OpenSettings();
        toolbar.Controls.Add(settingsButton, 3, 0);
        workspace.Controls.Add(toolbar, 0, 0);

        previewFrame = new PreviewSurface
        {
            Name = "previewSurface", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1,
            Padding = new Padding(0, 1, 0, 0), Margin = Padding.Empty
        };
        previewFrame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        previewFrame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        preview.Margin = Padding.Empty;
        previewFrame.Controls.Add(preview, 0, 0);
        workspaceTips = new WorkspaceToolTip(preview, () => appearancePalette);
        preview.Controls.Add(workspaceTips);
        foreach (CaptureModeButton command in modeButtons.Values.Append(edit).Append(copy).Append(save).Append(settingsButton))
            workspaceTips.Track(command);
        preview.SizeChanged += (_, _) => workspaceTips.HideHint();
        Deactivate += (_, _) => workspaceTips.HideHint();
        workspace.Controls.Add(previewFrame, 0, 1);
        status.Tag = "secondary";
        status.Margin = Padding.Empty;
        status.Padding = new Padding(14, 5, 14, 5);
        status.Visible = status.Text.Length > 0;
        status.TextChanged += (_, _) => status.Visible = status.Text.Length > 0;
        workspace.Controls.Add(status, 0, 2);
        Controls.Add(workspace);

        ApplyWorkspaceLanguage();
    }

    private static FlowLayoutPanel CommandGroup(string name) => new BufferedCommandGroup()
    {
        Name = name, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Left
    };

    private static void SetCommandLabel(CaptureModeButton button, string label)
    {
        button.Text = label;
        button.AccessibleName = label;
    }

    private void RefreshPreviewToolbar()
    {
        previewActions.Visible = copy.Enabled;
    }

    private void ApplyWorkspaceLanguage()
    {
        SetCommandLabel(settingsButton, L("Settings"));
        settingsButton.AccessibleDescription = L("SettingsDescription");
        foreach (CaptureShortcut action in ApplicationPreferences.Actions)
            SetCommandLabel(modeButtons[action.ModeKey], action.Title);
        SetCommandLabel(copy, L("Copy"));
        SetCommandLabel(save, L("Save"));
        SetCommandLabel(edit, L("Edit"));
        status.AccessibleName = L("CaptureStatus");
        UiLanguage.ApplyTextDirection(workspace);
        preview.ApplyViewportLanguage();
        preview.Invalidate();
        RefreshPreviewToolbar();
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
        using GraphicsPath path = CaptureDrawing.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), Palette.CornerRadius(10) * s);
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
        e.Graphics.Clear(Palette.Canvas);
        using SolidBrush border = new(Palette.Border);
        e.Graphics.FillRectangle(border, 0, 0, Width, Math.Max(1, Padding.Top));
    }
}
