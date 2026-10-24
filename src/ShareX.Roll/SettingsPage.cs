using static ShareX.Roll.Localization.UiText;
// GPL-3.0-or-later. In-window preferences and scrolling option presentation.
using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using System.Drawing.Drawing2D;

namespace ShareX.Roll;

internal sealed class SettingsPage : UserControl
{
    internal event EventHandler? ReturnRequested;
    internal event Action<AppearanceMode>? AppearancePreview;
    internal event Action<bool>? MotionPreview;
    private readonly ApplicationPreferences draft;
    private readonly Func<ApplicationPreferences, ScrollingCaptureOptions, string?> savePreferences;
    private readonly ScrollingOptionsEditor scrollingEditor;
    private readonly SettingsReveal scrollingReveal;
    private readonly SaveLocationOptions saveLocation;
    private readonly Dictionary<string, ShortcutRecorder> recorders = new();
    private readonly Dictionary<AppearanceMode, ThemeOption> themes = new();
    private readonly TableLayoutPanel layout = new();
    private readonly Label feedback = new() { Name = "settingsFeedback", AutoSize = true, Dock = DockStyle.Fill, Tag = "secondary", Margin = new Padding(0, 10, 0, 8) };
    private readonly Button save = ActionButton("saveSettingsButton", L("SaveReturn"), 124);
    private readonly Button cancel = ActionButton("cancelSettingsButton", L("Cancel"), 76);
    private readonly Panel scroller = new SettingsScrollViewport { AutoScroll = true, Margin = Padding.Empty };
    private readonly SettingsScrollHost scrollHost;
    private readonly TableLayoutPanel content = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
    private readonly SettingsGroup shortcuts = new();
    private readonly List<ModeGlyph> glyphs = new();
    private readonly MotionToggle motion = new() { Name = "animationsToggle", Size = new Size(46, 28), AccessibleName = L("Motion") };
    private readonly SettingsChoice language = new() { Name = "languageSelector", Width = 220, DropDownWidth = 380, MaxDropDownItems = 12, Anchor = AnchorStyles.Right, Margin = Padding.Empty };

    internal SettingsPage(ApplicationPreferences preferences, ScrollingCaptureOptions scrollingOptions, Func<ApplicationPreferences, ScrollingCaptureOptions, string?> savePreferences)
    {
        SuspendLayout();
        Name = "SettingsPage";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 11f);
        Size = new Size(960, 720);
        draft = preferences.Clone();
        this.savePreferences = savePreferences;
        scrollHost = new SettingsScrollHost(scroller) { Dock = DockStyle.Fill, Margin = Padding.Empty };

        layout.ColumnCount = 1;
        layout.RowCount = 3;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Margin = Padding.Empty;
        TableLayoutPanel heading = new() { Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(0, 76), ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        FlowLayoutPanel title = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
        title.Controls.Add(new Label { Text = L("Settings"), Font = new Font(Font.FontFamily, 18, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 5) });
        title.Controls.Add(new Label { Text = L("SettingsSubtitle"), AutoSize = true, Tag = "secondary", Margin = Padding.Empty });
        Button back = ActionButton("backToCaptureButton", L("Back"), 112);
        title.SizeChanged += (_, _) => WrapLabels(title, title.ClientSize.Width);
        back.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        back.Margin = new Padding(0, 8, 0, 0);
        back.Tag = "quiet";
        back.Click += (_, _) => ReturnRequested?.Invoke(this, EventArgs.Empty);
        heading.Controls.Add(title, 0, 0);
        heading.Controls.Add(back, 1, 0);
        layout.Controls.Add(heading, 0, 0);

        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control control)
        {
            int row = content.RowCount++;
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(control, 0, row);
        }
        Add(SectionHeading(L("Appearance"), L("AppearanceHint")));
        TableLayoutPanel appearance = new() { Name = "appearanceOptions", ColumnCount = 3, RowCount = 1, Dock = DockStyle.Top, Height = 78, Margin = new Padding(0, 0, 0, 12) };
        for (int i = 0; i < 3; i++)
        {
            AppearanceMode mode = (AppearanceMode)i;
            appearance.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            ThemeOption option = new()
            {
                Name = "theme" + mode, Mode = mode, Text = new[] { L("SystemTheme"), L("Light"), L("Dark") }[i],
                Dock = DockStyle.Fill, Margin = new Padding(i == 0 ? 0 : 5, 0, i == 2 ? 0 : 5, 0),
                Checked = draft.Appearance == mode, TabIndex = i
            };
            option.AccessibleName = option.Text + L("AppearanceSuffix");
            option.CheckedChanged += (_, _) =>
            {
                if (!option.Checked) return;
                draft.Appearance = mode;
                RefreshSystemAppearance();
                AppearancePreview?.Invoke(mode);
                ShowFeedback(L("AppearancePreview"));
            };
            themes.Add(mode, option);
            appearance.Controls.Add(option, i, 0);
        }
        Add(appearance);
        SettingsGroup motionRow = new() { Name = "motionRow", Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(0, 62), ColumnCount = 2, RowCount = 1, Padding = new Padding(14, 9, 14, 9), Margin = new Padding(0, 0, 0, 24) };
        motionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        motionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        motionRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        FlowLayoutPanel motionCaption = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        motionRow.SizeChanged += (_, _) => WrapLabels(motionCaption, motionRow.ClientSize.Width - motionRow.Padding.Horizontal - motion.Width);
        motionCaption.Controls.Add(new Label { Text = L("Motion"), AutoSize = true, Margin = Padding.Empty });
        motionCaption.Controls.Add(new Label { Text = L("MotionHint"), AutoSize = true, Tag = "secondary", Font = new Font(Font.FontFamily, 10), Margin = new Padding(0, 2, 0, 0) });
        motion.Checked = draft.AnimationsEnabled;
        motion.Anchor = AnchorStyles.Right;
        motion.Margin = Padding.Empty;
        motion.CheckedChanged += (_, _) =>
        {
            draft.AnimationsEnabled = motion.Checked;
            MotionPreview?.Invoke(motion.Checked);
            ShowFeedback(L("MotionPreview"));
        };
        motionRow.Controls.Add(motionCaption, 0, 0);
        motionRow.Controls.Add(motion, 1, 0);
        Add(motionRow);
        SettingsGroup languageRow = new() { Name = "languageRow", Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(0, 62), ColumnCount = 2, RowCount = 1, Padding = new Padding(14, 9, 14, 9), Margin = new Padding(0, 0, 0, 24) };
        languageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        languageRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        languageRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        FlowLayoutPanel languageCaption = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        languageRow.SizeChanged += (_, _) => WrapLabels(languageCaption, languageRow.ClientSize.Width - languageRow.Padding.Horizontal - language.Width);
        languageCaption.Controls.Add(new Label { Text = L("Language"), AutoSize = true, Margin = Padding.Empty });
        languageCaption.Controls.Add(new Label { Text = L("LanguageHint"), AutoSize = true, Tag = "secondary", Font = new Font(Font.FontFamily, 10), Margin = new Padding(0, 2, 0, 0) });
        language.Items.AddRange(UiLanguage.Options.Cast<object>().ToArray());
        language.AccessibleName = L("Language");
        language.SelectedItem = UiLanguage.Options.Single(option => option.Mode == draft.Language);
        language.SelectedIndexChanged += (_, _) =>
        {
            draft.Language = ((LanguageOption)language.SelectedItem!).Mode;
            ShowFeedback(L("LanguageFeedback"));
        };
        languageRow.Controls.Add(languageCaption, 0, 0);
        languageRow.Controls.Add(language, 1, 0);
        Add(languageRow);
        Add(SectionHeading(L("Saving"), ""));
        saveLocation = new SaveLocationOptions(draft) { Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 24) };
        saveLocation.Changed += (_, _) => ShowFeedback(L("Unsaved"));
        Add(saveLocation);
        SettingsGroup scrollingRow = new() { Name = "scrollingOptionsRow", DrawDividers = false, Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2, Padding = new Padding(14, 9, 14, 9), Margin = new Padding(0, 0, 0, 24) };
        scrollingRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scrollingRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        scrollingRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        SettingsDisclosure scrollingHeader = new() { Name = "scrollingOptionsButton", Text = L("Scroll"), AccessibleName = L("Scroll"), Dock = DockStyle.Fill, Margin = Padding.Empty };
        scrollingEditor = new ScrollingOptionsEditor(scrollingOptions) { Visible = false };
        scrollingReveal = new SettingsReveal(scrollingEditor);
        scrollingEditor.Changed += (_, _) => ShowFeedback(L("Unsaved"));
        scrollingHeader.Click += (_, _) => ToggleScrolling(scrollingHeader);
        scrollingRow.Controls.Add(scrollingHeader, 0, 0);
        scrollingRow.Controls.Add(scrollingReveal, 0, 1);
        Add(scrollingRow);
        Add(SectionHeading(L("Shortcuts"), L("ShortcutsHint")));
        shortcuts.Name = "shortcutList";
        shortcuts.Dock = DockStyle.Top;
        shortcuts.AutoSize = true;
        shortcuts.ColumnCount = 4;
        shortcuts.RowCount = ApplicationPreferences.Actions.Length;
        shortcuts.Padding = new Padding(14, 2, 10, 2);
        shortcuts.Margin = new Padding(0, 0, 0, 10);
        shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 226));
        shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        int index = 0;
        foreach (CaptureShortcut action in ApplicationPreferences.Actions)
        {
            shortcuts.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            ModeGlyph glyph = new() { Kind = action.Kind, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), TabStop = false };
            glyphs.Add(glyph);
            Label label = new() { Text = action.Title, AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            ShortcutRecorder recorder = new()
            {
                Name = action.Kind + "Shortcut", Dock = DockStyle.Fill, Margin = new Padding(0, 9, 8, 9),
                AccessibleName = action.Title + L("ShortcutSuffix")
            };
            recorder.SetShortcut(draft.Shortcuts[action.Kind]);
            recorder.Changed += (_, _) =>
            {
                draft.Shortcuts[action.Kind] = recorder.Shortcut;
                ShowFeedback(L("Unsaved"));
            };
            Button clear = ActionButton(action.Kind + "Clear", "×", 28);
            clear.Dock = DockStyle.Fill;
            clear.Margin = new Padding(2, 12, 2, 12);
            clear.AccessibleName = L("ClearPrefix") + action.Title + L("ShortcutSuffix");
            clear.AccessibleDescription = L("DisableDescription");
            clear.Click += (_, _) => recorder.SetShortcut(Keys.None);
            recorders.Add(action.Kind, recorder);
            shortcuts.Controls.Add(glyph, 0, index);
            shortcuts.Controls.Add(label, 1, index);
            shortcuts.Controls.Add(recorder, 2, index);
            shortcuts.Controls.Add(clear, 3, index++);
        }
        Add(shortcuts);
        Add(new Label
        {
            Text = L("SupportedKeys"),
            AutoSize = true, Dock = DockStyle.Top, Tag = "secondary", Font = new Font(Font.FontFamily, 10),
            Margin = new Padding(0, 2, 0, 8)
        });
        Control aboutHeading = SectionHeading(L("About"), "");
        aboutHeading.Margin = new Padding(0, 16, 0, 12);
        Add(aboutHeading);
        Add(CreateAboutSection());
        scroller.Controls.Add(content);
        layout.Controls.Add(scrollHost, 0, 1);

        TableLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        feedback.Font = new Font(Font.FontFamily, 10);
        footer.Controls.Add(feedback, 0, 0);
        TableLayoutPanel actions = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = Padding.Empty };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Button reset = ActionButton("resetSettingsButton", L("Reset"), 100);
        reset.Anchor = AnchorStyles.Left;
        reset.Tag = cancel.Tag = "quiet";
        reset.Click += (_, _) =>
        {
            ApplicationPreferences defaults = new();
            foreach (CaptureShortcut action in ApplicationPreferences.Actions) recorders[action.Kind].SetShortcut(defaults.Shortcuts[action.Kind]);
            themes[defaults.Appearance].Checked = true;
            motion.Checked = defaults.AnimationsEnabled;
            language.SelectedItem = UiLanguage.Options.Single(option => option.Mode == defaults.Language);
            saveLocation.LoadPreferences(defaults);
            scrollingEditor.LoadOptions(new ScrollingCaptureOptions());
            ShowFeedback(L("ResetFeedback"));
        };
        cancel.Click += (_, _) => ReturnRequested?.Invoke(this, EventArgs.Empty);
        save.Click += (_, _) => SaveAndReturn();
        FlowLayoutPanel buttons = new() { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        cancel.Margin = new Padding(0, 0, 10, 0);
        buttons.Controls.AddRange([cancel, save]);
        actions.Controls.Add(reset, 0, 0);
        actions.Controls.Add(buttons, 1, 0);
        footer.Controls.Add(actions, 0, 1);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);
        VisibleChanged += (_, _) => { if (!Visible) EndScrollingReveal(); };
        RefreshSystemAppearance();
        ShowFeedback(L("SaveHint"));
        UiLanguage.ApplyTextDirection(this);
        ResumeLayout(true);
        ArrangePage();
    }

    private static Button ActionButton(string name, string text, int width) => new()
    {
        Name = name, Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(width, 36), Padding = new Padding(8, 0, 8, 0), Margin = Padding.Empty,
        Cursor = Cursors.Hand, FlatStyle = FlatStyle.Flat
    };

    private Control SectionHeading(string title, string hint)
    {
        FlowLayoutPanel heading = new() { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 0, 0, 12) };
        heading.SizeChanged += (_, _) => WrapLabels(heading, heading.ClientSize.Width);
        heading.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 16, 0) });
        heading.Controls.Add(new Label { Text = hint, AutoSize = true, Tag = "secondary", Font = new Font(Font.FontFamily, 10), Margin = new Padding(0, 2, 0, 0) });
        return heading;
    }

    private static void WrapLabels(Control caption, int width)
    {
        foreach (Label label in caption.Controls.OfType<Label>())
            label.MaximumSize = new Size(Math.Max(1, width - label.Margin.Horizontal), 0);
    }

    private SettingsGroup CreateAboutSection() => SettingsAbout.Create(Font, ShowFeedback);

    private void SaveAndReturn()
    {
        if (draft.ValidateSaveFolder() is string folderError)
        {
            ShowFeedback(L("NotSaved") + folderError);
            saveLocation.FocusFolder();
            return;
        }
        string? error = savePreferences(draft.Clone(), scrollingEditor.ReadOptions());
        if (error != null) { ShowFeedback(L("NotSaved") + error); return; }
        ReturnRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ShowFeedback(string message)
    {
        feedback.Text = message;
        feedback.AccessibleName = message;
    }

    internal void RefreshSystemAppearance()
    {
        EndScrollingReveal();
        UiMotion.UserEnabled = draft.AnimationsEnabled;
        CapturePalette palette = CapturePalette.For(draft.Appearance);
        void Paint(Control control, Color background)
        {
            if (control is SettingsGroup group) { background = palette.Surface; group.Palette = palette; group.Invalidate(); }
            control.BackColor = background;
            control.ForeColor = Equals(control.Tag, "secondary") ? palette.Secondary : palette.Text;
            if (control.Name == "aboutDivider") control.BackColor = palette.Border;
            if (control is SettingsFolderField folderField) { folderField.ApplyPalette(palette); return; }
            if (control is SettingsNumberField number) { number.ApplyPalette(palette); return; }
            if (control is SettingsChoice choice) { choice.ApplyPalette(palette); return; }
            if (control is AboutLinkButton aboutLink) { aboutLink.ApplyPalette(palette); return; }
            if (control is AppMark mark) { mark.Palette = palette; mark.Invalidate(); }
            if (control is LinkLabel link)
            {
                link.LinkColor = link.VisitedLinkColor = palette.Accent;
                link.ActiveLinkColor = palette.Text;
            }
            if (control is ShortcutRecorder recorder) { recorder.Palette = palette; recorder.Invalidate(); }
            else if (control is SettingsDisclosure disclosure) { disclosure.Palette = palette; disclosure.Invalidate(); }
            else if (control is Button button)
            {
                string? accessibleName = button.AccessibleName;
                palette.StyleButton(button, ReferenceEquals(button, save));
                if (button.Text == "×") button.AccessibleName = accessibleName;
            }
            if (control is ThemeOption theme) { theme.Palette = palette; theme.Invalidate(); }
            if (control is MotionToggle toggle) { toggle.Palette = palette; toggle.Invalidate(); }
            foreach (Control child in control.Controls) Paint(child, background);
        }
        Paint(this, palette.Background);
        scrollHost.ApplyPalette(palette);
        shortcuts.Palette = palette;
        shortcuts.Invalidate();
        foreach (ModeGlyph glyph in glyphs) { glyph.Ink = palette.Secondary; glyph.Invalidate(); }
    }

    private void ArrangePage()
    {
        if (layout == null) return;
        float scale = DeviceDpi / 96f;
        int gap = (int)(28 * scale);
        int width = Math.Max(1, Math.Min(ClientSize.Width - gap * 2, (int)(760 * scale)));
        layout.Bounds = new Rectangle((ClientSize.Width - width) / 2, gap, width, Math.Max(1, ClientSize.Height - gap * 2));
    }

    protected override void OnResize(EventArgs e) { EndScrollingReveal(); base.OnResize(e); ArrangePage(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); ArrangePage(); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { ReturnRequested?.Invoke(this, EventArgs.Empty); return true; }
        if (keyData == (Keys.Control | Keys.Return)) { SaveAndReturn(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void EndScrollingReveal()
    {
        scrollingReveal?.Finish();
    }

    private void ToggleScrolling(SettingsDisclosure header)
    {
        header.Expanded = !header.Expanded;
        scrollingReveal.SetExpanded(header.Expanded);
        header.NotifyExpanded();
    }

    protected override void Dispose(bool disposing) { if (disposing) EndScrollingReveal(); base.Dispose(disposing); }
}

internal sealed class SettingsGroup : TableLayoutPanel
{
    internal bool DrawDividers { get; set; } = true;
    internal CapturePalette Palette { get; set; } = CapturePalette.Light;
    internal SettingsGroup() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Palette.Background);
        using GraphicsPath path = CaptureDrawing.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 16 * DeviceDpi / 96f);
        using SolidBrush brush = new(Palette.Surface);
        using Pen pen = new(Palette.Border);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
        if (!DrawDividers) return;
        int y = Padding.Top;
        foreach (int height in GetRowHeights().Take(RowCount - 1))
        {
            y += height;
            g.DrawLine(pen, Padding.Left, y, Width - Padding.Right, y);
        }
    }
}

internal sealed class ModeGlyph : Control
{
    internal string Kind { get; init; } = "region";
    internal Color Ink { get; set; }
    protected override void OnPaint(PaintEventArgs e)
    {
        float size = 18 * DeviceDpi / 96f;
        CaptureDrawing.Icon(e.Graphics, Kind, new RectangleF(0, (Height - size) / 2, size, size), Ink);
    }
}

internal sealed class ThemeOption : RadioButton
{
    internal AppearanceMode Mode { get; init; }
    internal CapturePalette Palette { get; set; } = CapturePalette.Light;
    private readonly UiTween selection, hover;
    internal ThemeOption()
    {
        Appearance = Appearance.Button;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        selection = new(this, Invalidate);
        hover = new(this, Invalidate);
    }
    protected override void OnMouseEnter(EventArgs e) { hover.SpringTo(1); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover.SpringTo(0); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); selection?.SpringTo(Checked ? 1 : 0); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        CapturePalette p = Palette;
        float s = DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(p.Background);
        using GraphicsPath card = CaptureDrawing.Rounded(new RectangleF(s, s, Width - 2 * s, Height - 2 * s), 16 * s);
        using SolidBrush fill = new(CaptureDrawing.Blend(CaptureDrawing.Blend(p.Surface, p.Hover, hover.Value * .5f), p.Tint, selection.Value));
        using Pen edge = new(Checked || Focused ? p.Accent : p.Border, (Focused && ShowFocusCues ? 2 : Checked ? 1.5f : 1) * s);
        g.FillPath(fill, card);
        g.DrawPath(edge, card);
        RectangleF mini = new(14 * s, (Height - 42 * s) / 2, 58 * s, 42 * s);
        CapturePalette sample = Mode == AppearanceMode.Dark ? CapturePalette.Dark : CapturePalette.Light;
        if (SystemInformation.HighContrast) sample = p;
        using SolidBrush window = new(sample.Canvas);
        using SolidBrush bar = new(sample.Surface);
        using Pen line = new(sample.Border, s);
        g.FillRectangle(window, mini);
        g.FillRectangle(bar, mini.X, mini.Y, mini.Width, 10 * s);
        if (Mode == AppearanceMode.System && !SystemInformation.HighContrast)
        {
            using SolidBrush dark = new(CapturePalette.Dark.Surface);
            g.FillRectangle(dark, mini.X + mini.Width / 2, mini.Y, mini.Width / 2, mini.Height);
        }
        g.DrawRectangle(line, mini.X, mini.Y, mini.Width, mini.Height);
        using SolidBrush accent = new(p.Accent);
        g.FillRectangle(accent, mini.X + 7 * s, mini.Y + 17 * s, 12 * s, 14 * s);
        g.DrawLine(line, mini.X + 25 * s, mini.Y + 19 * s, mini.Right - 6 * s, mini.Y + 19 * s);
        g.DrawLine(line, mini.X + 25 * s, mini.Y + 26 * s, mini.Right - 12 * s, mini.Y + 26 * s);
        TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(84 * s), 0, Width - (int)(100 * s), Height), p.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | UiLanguage.TextDirection);
    }
}

internal sealed class ShortcutRecorder : Button
{
    internal Keys Shortcut { get; private set; }
    internal event EventHandler? Changed;
    internal CapturePalette Palette { get; set; } = CapturePalette.Light;
    private bool recording, hovered;

    internal ShortcutRecorder()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
    }
    internal void SetShortcut(Keys keys)
    {
        Shortcut = keys;
        recording = false;
        Text = keys == Keys.None ? L("Unset") : new HotkeyInfo(keys).ToString();
        AccessibleDescription = Text + L("RecorderDescription");
        Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        recording = !recording;
        if (recording) { Text = L("PressShortcut"); Invalidate(); }
        else SetShortcut(Shortcut);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!recording) return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == Keys.Escape) { SetShortcut(Shortcut); return true; }
        if ((keyData & Keys.KeyCode) == Keys.Tab) { SetShortcut(Shortcut); return base.ProcessCmdKey(ref msg, keyData); }
        if (ApplicationPreferences.IsSupported(keyData)) SetShortcut(keyData);
        return true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (recording)
        {
            if (ApplicationPreferences.IsSupported(e.KeyData)) SetShortcut(e.KeyData);
            e.SuppressKeyPress = true;
            return;
        }
        base.OnKeyDown(e);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (recording && e.KeyCode == Keys.PrintScreen) { SetShortcut(e.KeyData); e.SuppressKeyPress = true; return; }
        base.OnKeyUp(e);
    }
    protected override void OnLostFocus(EventArgs e) { if (recording) SetShortcut(Shortcut); Invalidate(); base.OnLostFocus(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        CapturePalette p = Palette;
        float s = DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(p.Surface);
        using GraphicsPath field = CaptureDrawing.Rounded(new RectangleF(s, s, Width - 2 * s, Height - 2 * s), 6 * s);
        using SolidBrush background = new(recording ? p.Tint : hovered ? p.Hover : p.Surface);
        g.FillPath(background, field);
        if (recording || Focused)
        {
            using Pen edge = new(p.Accent, s);
            g.DrawPath(edge, field);
        }
        if (recording || Shortcut == Keys.None)
        {
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, recording ? p.Accent : p.Secondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | UiLanguage.TextDirection);
            return;
        }
        string[] keys = Text.Split(" + ");
        using Font keyFont = new("Segoe UI", 10);
        int[] widths = keys.Select(k => TextRenderer.MeasureText(k, keyFont).Width + (int)(9 * s)).ToArray();
        int gap = (int)(6 * s);
        int total = widths.Sum() + (widths.Length - 1) * gap;
        if (total > Width - 8 * s)
        {
            TextRenderer.DrawText(g, Text, keyFont, ClientRectangle, p.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }
        float x = (Width - total) / 2f, height = 25 * s, top = (Height - height) / 2;
        using SolidBrush cap = new(p.Canvas);
        using Pen border = new(p.Border, s);
        for (int i = 0; i < keys.Length; i++)
        {
            using GraphicsPath shape = CaptureDrawing.Rounded(new RectangleF(x, top, widths[i], height), 4 * s);
            g.FillPath(cap, shape);
            g.DrawPath(border, shape);
            TextRenderer.DrawText(g, keys[i], keyFont, new Rectangle((int)x, (int)top, widths[i], (int)height), p.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x += widths[i] + gap;
        }
    }
}
