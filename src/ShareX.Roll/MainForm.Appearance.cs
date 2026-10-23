using static ShareX.Roll.Localization.UiText;
// GPL-3.0-or-later. Layout and appearance are kept separate from the capture workflow.
using Microsoft.Win32;
using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;

namespace ShareX.Roll;

public sealed partial class MainForm
{
    private readonly Dictionary<Keys, CaptureModeButton> modeButtons = new();
    private readonly Label previewTitle = new() { Text = L("Preview"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label previewHint = new() { Text = L("Waiting"), AutoSize = true, Anchor = AnchorStyles.Left };
    private TableLayoutPanel workspace = null!;
    private TableLayoutPanel previewFrame = null!;
    private CapturePalette appearancePalette = CapturePalette.Current;
    private bool? frameUsesDarkMode;

    private void InitializeAppearance()
    {
        SuspendLayout();
        Font = new Font("Microsoft YaHei UI", 11);
        DoubleBuffered = true;
        using (Stream stream = typeof(MainForm).Assembly.GetManifestResourceStream("SnapFlow.AppIcon")!)
        using (Icon icon = new(stream)) ShareXResources.Icon = (Icon)icon.Clone();
        Icon = ShareXResources.Icon;
        HandleCreated += (_, _) =>
        {
            frameUsesDarkMode = null;
            ApplyWindowFrameAppearance();
        };
        UiMotion.UserEnabled = preferences.AnimationsEnabled;
        InitializeWorkspaceLayout();
        VisibleChanged += (_, _) => { if (!Visible) EndPageTransition(); };
        copy.EnabledChanged += (_, _) => RefreshPreviewToolbar();
        ApplyAppearance();
        SystemEvents.UserPreferenceChanged += AppearancePreferenceChanged;
        Disposed += (_, _) => SystemEvents.UserPreferenceChanged -= AppearancePreferenceChanged;
        ResumeLayout(true);
    }

    private void UpdateShortcutLabel(Keys key, HotkeyInfo hotkey)
    {
        if (!modeButtons.TryGetValue(key, out CaptureModeButton? button)) return;
        button.Shortcut = hotkey.Hotkey == Keys.None ? L("NoShortcut") : hotkey.Status == HotkeyStatus.Registered
            ? hotkey.ToString().Replace(" + ", "+") : L("ShortcutUnavailable");
        button.AccessibleDescription = button.Shortcut;
        button.Invalidate();
    }

    private void AppearancePreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!IsHandleCreated || IsDisposed || Disposing) return;
        try { BeginInvoke((Action)ApplyAppearance); }
        catch (InvalidOperationException) { /* The window was closed while the system event was queued. */ }
    }

    private void ApplyAppearance()
    {
        if (IsDisposed || Disposing) return;
        UiMotion.UserEnabled = preferences.AnimationsEnabled;
        ApplyPalette(CapturePalette.For(preferences.Appearance));
    }

    private void ApplyPalette(CapturePalette palette)
    {
        appearancePalette = palette;
        palette.ApplyLibraryTheme();
        // Recolor the client area without resetting the icon or repainting the native frame.
        ShareXResources.ApplyCustomThemeToControl(this);
        ApplyWindowFrameAppearance();
        BackColor = palette.Background;
        PaintContainers(workspace, palette, palette.Background);
        PaintContainers(previewFrame, palette, palette.Surface);
        ((PreviewSurface)previewFrame).Palette = palette;
        previewFrame.Invalidate();
        preview.BackColor = palette.Canvas;
        ((PreviewCanvas)preview).Palette = palette;
        preview.ApplyViewportAppearance();
        foreach (CaptureModeButton button in modeButtons.Values) { button.Palette = palette; button.Invalidate(); }
        palette.StyleButton(copy);
        palette.StyleButton(save, true);
        palette.StyleButton(edit);
        palette.StyleButton(settingsButton);
        preview.Invalidate();
        if (scrollForm is { IsDisposed: false }) ApplyScrollingColors(scrollForm, palette);
        // The library theme walks all child controls. Restore the settings surfaces afterwards.
        settingsPage?.RefreshSystemAppearance();
    }

    private void ApplyWindowFrameAppearance()
    {
        if (!IsHandleCreated) return;
        bool dark = ColorHelpers.IsDarkColor(appearancePalette.Background);
        if (frameUsesDarkMode != dark && NativeMethods.UseImmersiveDarkMode(Handle, dark))
            frameUsesDarkMode = dark;
    }

    private static void PaintContainers(Control parent, CapturePalette palette, Color background)
    {
        parent.BackColor = background;
        parent.ForeColor = Equals(parent.Tag, "secondary") ? palette.Secondary : palette.Text;
        foreach (Control child in parent.Controls) PaintContainers(child, palette, background);
    }

    private static void ApplyScrollingColors(Form form, CapturePalette palette)
    {
        ShareXResources.ApplyTheme(form);
        PaintContainers(form, palette, palette.Background);
        Control? output = form.Controls.Find("pOutput", true).FirstOrDefault();
        if (output != null) output.BackColor = palette.Canvas;
        foreach (string name in new[] { "btnCapture", "btnUpload", "btnCopy", "btnOptions", "btnHelp" })
            if (form.Controls.Find(name, true).FirstOrDefault() is Button button) palette.StyleButton(button, name == "btnCapture");
    }

    private void ApplyScrollingAppearance(ScrollingCaptureForm form)
    {
        // Reparent the existing controls only. Their click handlers, scroll/pan behavior and lifetimes are unchanged.
        Control Find(string name) => form.Controls.Find(name, true).Single();
        Control output = Find("pOutput");
        Control dimensions = Find("lblResultSize");
        Control indicator = Find("pbStatus");
        Control[] buttons = new[] { "btnCapture", "btnUpload", "btnCopy", "btnOptions", "btnHelp" }.Select(Find).ToArray();
        form.SuspendLayout();
        form.Font = Font;
        float scale = form.DeviceDpi / 96f;
        int D(int value) => (int)Math.Round(value * scale);
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(D(20)), Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        TableLayoutPanel heading = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, D(14)) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Label title = new() { Text = L("Scroll"), Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold), AutoSize = true, Margin = Padding.Empty };
        heading.Controls.Add(title, 0, 0);
        dimensions.Dock = DockStyle.None;
        dimensions.Anchor = AnchorStyles.Right;
        dimensions.AutoSize = true;
        dimensions.MinimumSize = new Size(D(110), D(26));
        heading.Controls.Add(dimensions, 1, 0);
        indicator.Anchor = AnchorStyles.None;
        indicator.Margin = new Padding(D(8));
        heading.Controls.Add(indicator, 2, 0);
        FlowLayoutPanel toolbar = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, D(16)) };
        foreach (Control control in buttons)
        {
            Button button = (Button)control;
            button.Dock = DockStyle.None;
            button.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            button.AutoSize = true;
            button.MinimumSize = new Size(D(button.Name == "btnHelp" ? 40 : 112), D(38));
            button.Size = button.MinimumSize;
            button.Padding = new Padding(D(10), D(4), D(10), D(4));
            button.Margin = new Padding(0, 0, D(10), D(4));
            toolbar.Controls.Add(button);
        }
        output.Dock = DockStyle.Fill;
        output.Margin = Padding.Empty;
        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(toolbar, 0, 1);
        root.Controls.Add(output, 0, 2);
        form.Controls.Add(root);
        ApplyScrollingColors(form, appearancePalette);
        form.ResumeLayout(true);
    }
}
