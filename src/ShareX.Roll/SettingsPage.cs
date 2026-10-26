using static ShareX.Roll.Localization.UiText;
// GPL-3.0-or-later. In-window preferences and scrolling option presentation.
using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ShareX.Roll;

internal sealed class SettingsPage : UserControl, IBackdropSnapshotOverlay, IBackdropCachingScope
{
    internal event EventHandler? ReturnRequested;
    internal event Action<AppearanceMode>? AppearancePreview;
    internal event Action<bool>? MotionPreview;
    internal event Action<bool>? TransparencyPreview;
    private readonly ApplicationPreferences draft;
    private readonly Func<ApplicationPreferences, ScrollingCaptureOptions, string?> savePreferences;
    private readonly ScrollingOptionsEditor scrollingEditor;
    private readonly SettingsReveal scrollingReveal;
    private readonly SaveLocationOptions saveLocation;
    private readonly Dictionary<string, ShortcutRecorder> recorders = new();
    private readonly Dictionary<AppearanceMode, ThemeOption> themes = new();
    private readonly TableLayoutPanel heading;
    private readonly TableLayoutPanel footer;
    private readonly TableLayoutPanel scrollCanvas = new BackgroundLayout { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty };
    private bool arrangingOverlay, initializing = true, restoring;
    private readonly Label feedback = new() { Name = "settingsFeedback", AutoSize = true, Dock = DockStyle.Fill, Tag = "secondary", Margin = new Padding(0, 10, 0, 8) };
    private readonly Button save = ActionButton("saveSettingsButton", L("SaveReturn"), 124);
    private readonly Button cancel = ActionButton("cancelSettingsButton", L("Cancel"), 76);
    private readonly Panel scroller = new OverlayViewport { AutoScroll = true, Margin = Padding.Empty };
    private readonly SettingsScrollHost scrollHost;
    private readonly SettingsBarBackdrop headingBackdrop;
    private readonly SettingsBarBackdrop footerBackdrop;
    private readonly TableLayoutPanel content = new BackgroundLayout { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
    private readonly TableLayoutPanel shortcuts = new();
    private readonly List<ModeGlyph> glyphs = new();
    private readonly MotionToggle motion = new() { Name = "animationsToggle", Size = new Size(46, 28), AccessibleName = L("Motion") };
    private readonly MotionToggle transparency = new() { Name = "transparencyToggle", Size = new Size(46, 28), AccessibleName = L("Transparency") };
    private readonly Label transparencyHint = new() { Name = "transparencyHint", AutoSize = true, Tag = "secondary", Margin = new Padding(0, 2, 0, 0) };
    private readonly SettingsChoice language = new() { Name = "languageSelector", Width = 220, DropDownWidth = 380, MaxDropDownItems = 12, Anchor = AnchorStyles.Right, Margin = Padding.Empty };

    internal SettingsPage(ApplicationPreferences preferences, ScrollingCaptureOptions scrollingOptions, Func<ApplicationPreferences, ScrollingCaptureOptions, string?> savePreferences)
    {
        SuspendLayout();
        content.SuspendLayout();
        scrollCanvas.SuspendLayout();
        shortcuts.SuspendLayout();
        Name = "SettingsPage";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 11f);
        Size = new Size(960, 720);
        draft = preferences.Clone();
        this.savePreferences = savePreferences;
        scrollHost = new SettingsScrollHost(scroller) { Dock = DockStyle.Fill, Margin = Padding.Empty, PresentScrollFrame = PresentScrollFrame };

        heading = new() { Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(0, 76), ColumnCount = 1, RowCount = 1, Margin = Padding.Empty };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        FlowLayoutPanel title = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
        title.Controls.Add(new Label { Text = L("Settings"), Font = new Font(Font.FontFamily, 18, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 5) });
        title.Controls.Add(new Label { Text = L("SettingsSubtitle"), AutoSize = true, Tag = "secondary", Margin = Padding.Empty });
        title.SizeChanged += (_, _) => WrapLabels(title, title.ClientSize.Width);
        heading.Controls.Add(title, 0, 0);

        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control control)
        {
            int row = content.RowCount++;
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(control, 0, row);
        }
        Add(SectionHeading(L("Appearance"), ""));
        TableLayoutPanel appearance = new() { Name = "appearanceOptions", ColumnCount = 5, RowCount = 1, Dock = DockStyle.Top, Height = 88, Margin = new Padding(0, 0, 0, SettingsLayout.Gap) };
        for (int i = 0; i < 3; i++)
        {
            AppearanceMode mode = (AppearanceMode)i;
            appearance.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            if (i < 2) appearance.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SettingsLayout.Gap));
            ThemeOption option = new()
            {
                Name = "theme" + mode, Mode = mode, Text = new[] { L("SystemTheme"), L("Light"), L("Dark") }[i],
                Dock = DockStyle.Fill, Margin = Padding.Empty,
                Checked = draft.Appearance == mode, TabIndex = i
            };
            option.AccessibleName = option.Text + L("AppearanceSuffix");
            option.CheckedChanged += (_, _) =>
            {
                if (!option.Checked) return;
                draft.Appearance = mode;
                if (restoring) return;
                ShowFeedback(L("AppearancePreview"));
                if (AppearancePreview is { } preview) preview(mode);
                else RefreshSystemAppearance();
            };
            themes.Add(mode, option);
            appearance.Controls.Add(option, i * 2, 0);
        }
        Add(appearance);
        SettingsGroup transparencyRow = new() { Name = "transparencyRow", Dock = DockStyle.Top };
        Label transparencyTitle = new() { Text = L("Transparency"), AutoSize = true, Margin = Padding.Empty };
        transparencyHint.Font = new Font(Font.FontFamily, 10);
        FlowLayoutPanel transparencyCaption = SettingsLayout.ConfigureOption(transparencyRow, transparencyTitle, transparencyHint, transparency);
        transparencyCaption.Name = "transparencyCaption";
        transparencyCaption.Cursor = Cursors.Hand;
        SettingsLayout.SetBottomGap(transparencyRow);
        transparency.Checked = draft.TransparencyEnabled;
        transparency.CheckedChanged += (_, _) =>
        {
            draft.TransparencyEnabled = transparency.Checked;
            if (restoring) return;
            RefreshBarMaterials();
            TransparencyPreview?.Invoke(transparency.Checked);
            ShowFeedback(L("TransparencyPreview"));
        };
        foreach (Control caption in new Control[] { transparencyCaption, transparencyTitle, transparencyHint })
            caption.Click += (_, _) => { transparency.Focus(); transparency.Checked = !transparency.Checked; };
        Add(transparencyRow);
        SettingsGroup motionRow = new() { Name = "motionRow", Dock = DockStyle.Top };
        SettingsLayout.ConfigureOption(motionRow, new Label { Text = L("Motion") },
            new Label { Text = L("MotionHint"), Tag = "secondary", Font = new Font(Font.FontFamily, 10) }, motion);
        SettingsLayout.SetBottomGap(motionRow);
        motion.Checked = draft.AnimationsEnabled;
        motion.CheckedChanged += (_, _) =>
        {
            draft.AnimationsEnabled = motion.Checked;
            if (restoring) return;
            MotionPreview?.Invoke(motion.Checked);
            ShowFeedback(L("MotionPreview"));
        };
        Add(motionRow);
        SettingsGroup languageRow = new() { Name = "languageRow", Dock = DockStyle.Top };
        SettingsLayout.ConfigureOption(languageRow, new Label { Text = L("Language") },
            new Label { Text = L("LanguageHint"), Tag = "secondary", Font = new Font(Font.FontFamily, 10) }, language);
        SettingsLayout.SetBottomGap(languageRow, SettingsLayout.SectionGap);
        language.Items.AddRange(UiLanguage.Options.Cast<object>().ToArray());
        language.AccessibleName = L("Language");
        language.SelectedItem = UiLanguage.Options.Single(option => option.Mode == draft.Language);
        language.SelectedIndexChanged += (_, _) =>
        {
            draft.Language = ((LanguageOption)language.SelectedItem!).Mode;
            ShowFeedback(L("LanguageFeedback"));
        };
        Add(languageRow);
        Add(SectionHeading(L("Saving"), ""));
        saveLocation = new SaveLocationOptions(draft) { Dock = DockStyle.Top };
        SettingsLayout.SetBottomGap(saveLocation);
        SettingsLayout.MatchOptionHeights(this, transparencyRow, motionRow, languageRow, saveLocation.Header);
        saveLocation.Changed += (_, _) => ShowFeedback(L("Unsaved"));
        Add(saveLocation);
        SettingsGroup scrollingRow = new() { Name = "scrollingOptionsRow", DrawDividers = false, Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2, Padding = Padding.Empty, Margin = new Padding(0, 0, 0, SettingsLayout.SectionGap) };
        scrollingRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scrollingRow.RowStyles.Add(new RowStyle(SizeType.Absolute, SettingsLayout.RowHeight));
        scrollingRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        SettingsDisclosure scrollingHeader = new() { Name = "scrollingOptionsButton", Text = L("Scroll"), AccessibleName = L("Scroll"), Dock = DockStyle.Fill, Margin = Padding.Empty };
        scrollingEditor = new ScrollingOptionsEditor(scrollingOptions) { Visible = false };
        scrollingReveal = new SettingsReveal(scrollingEditor) { PresentFrame = PresentScrollFrame };
        scrollingReveal.Margin = new Padding(SettingsLayout.Inset, 0, SettingsLayout.Inset, 0);
        scrollingEditor.Changed += (_, _) => ShowFeedback(L("Unsaved"));
        scrollingHeader.Click += (_, _) => ToggleScrolling(scrollingHeader);
        scrollingRow.Controls.Add(scrollingHeader, 0, 0);
        scrollingRow.Controls.Add(scrollingReveal, 0, 1);
        Add(scrollingRow);
        Add(SectionHeading(L("Shortcuts"), L("ShortcutsHint")));
        shortcuts.Name = "shortcutList";
        shortcuts.Dock = DockStyle.Top;
        shortcuts.AutoSize = true;
        shortcuts.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        shortcuts.ColumnCount = 1;
        shortcuts.RowCount = ApplicationPreferences.Actions.Length;
        shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        SettingsLayout.SetBottomGap(shortcuts);
        int index = 0;
        foreach (CaptureShortcut action in ApplicationPreferences.Actions)
        {
            shortcuts.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            SettingsGroup row = new()
            {
                Name = action.Kind + "ShortcutRow", Dock = DockStyle.Top,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(0, SettingsLayout.RowHeight), ColumnCount = 4, RowCount = 1,
                // The recorder already reserves a 6px shadow halo above and below its face.
                Padding = new Padding(SettingsLayout.Inset, 6, SettingsLayout.Inset, 6)
            };
            SettingsLayout.SetBottomGap(row, index < ApplicationPreferences.Actions.Length - 1 ? SettingsLayout.Gap : 0);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 232));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, ButtonSpacing.ToolHeight));
            ModeGlyph glyph = new() { Kind = action.Kind, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), TabStop = false };
            glyphs.Add(glyph);
            Label label = new() { Text = action.Title, AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            ShortcutRecorder recorder = new()
            {
                Name = action.Kind + "Shortcut", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0),
                AccessibleName = action.Title + L("ShortcutSuffix")
            };
            recorder.SetShortcut(draft.Shortcuts[action.Kind]);
            ShortcutClearButton clear = new()
            {
                Name = action.Kind + "Clear", Anchor = AnchorStyles.None,
                Margin = Padding.Empty,
                AccessibleName = L("ClearPrefix") + action.Title + L("ShortcutSuffix"),
                AccessibleDescription = L("DisableDescription"),
                Enabled = recorder.Shortcut != Keys.None
            };
            void ResizeShortcutRow()
            {
                if (recorder.IsHandleCreated)
                    row.RowStyles[0].Height = Math.Max(recorder.PreferredSize.Height, Math.Max(label.PreferredSize.Height, clear.Height));
            }
            recorder.HandleCreated += (_, _) => ResizeShortcutRow();
            recorder.FontChanged += (_, _) => ResizeShortcutRow();
            recorder.DpiChangedAfterParent += (_, _) => ResizeShortcutRow();
            recorder.Changed += (_, _) =>
            {
                draft.Shortcuts[action.Kind] = recorder.Shortcut;
                clear.Enabled = recorder.Shortcut != Keys.None;
                ShowFeedback(L("Unsaved"));
            };
            clear.Click += (_, _) => recorder.SetShortcut(Keys.None);
            recorders.Add(action.Kind, recorder);
            row.Controls.Add(glyph, 0, 0);
            row.Controls.Add(label, 1, 0);
            row.Controls.Add(recorder, 2, 0);
            row.Controls.Add(clear, 3, 0);
            shortcuts.Controls.Add(row, 0, index++);
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
        scrollCanvas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scrollCanvas.Controls.Add(content, 0, 0);
        scroller.Controls.Add(scrollCanvas);

        footer = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
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
            transparency.Checked = defaults.TransparencyEnabled;
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
        headingBackdrop = new SettingsBarBackdrop(heading, content);
        footerBackdrop = new SettingsBarBackdrop(footer, content);
        Controls.Add(scrollHost);
        Controls.Add(heading);
        Controls.Add(footer);
        heading.TabIndex = 0;
        scrollHost.TabIndex = 1;
        footer.TabIndex = 2;
        heading.Dock = footer.Dock = DockStyle.None;
        heading.BringToFront();
        footer.BringToFront();
        heading.Layout += (_, _) => ArrangePage();
        footer.Layout += (_, _) => ArrangePage();
        scrollHost.Controls["settingsScrollBar"]!.VisibleChanged += (_, _) => ArrangePage();
        VisibleChanged += (_, _) => { if (!Visible) EndScrollingReveal(); };
        RefreshSystemAppearance();
        ShowFeedback(L("SaveHint"));
        UiLanguage.ApplyTextDirection(this);
        shortcuts.ResumeLayout(true);
        content.ResumeLayout(true);
        scrollCanvas.ResumeLayout(true);
        initializing = false;
        ResumeLayout(true);
        ArrangePage();
    }

    private static Button ActionButton(string name, string text, int width) => new()
    {
        Name = name, Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(width, ButtonSpacing.ActionHeight), Padding = ButtonSpacing.ActionPadding, Margin = Padding.Empty,
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
        if (restoring) return;
        feedback.Text = message;
        feedback.AccessibleName = message;
    }

    internal void RestorePreferences(ApplicationPreferences preferences, ScrollingCaptureOptions scrollingOptions, bool refreshAppearance = true)
    {
        restoring = true;
        SuspendLayout();
        try
        {
            draft.Appearance = preferences.Appearance;
            draft.AnimationsEnabled = preferences.AnimationsEnabled;
            draft.TransparencyEnabled = preferences.TransparencyEnabled;
            draft.Language = preferences.Language;
            draft.UseDefaultSaveFolder = preferences.UseDefaultSaveFolder;
            draft.DefaultSaveFolder = preferences.DefaultSaveFolder;
            draft.Shortcuts = new(preferences.Shortcuts);
            foreach (CaptureShortcut action in ApplicationPreferences.Actions)
                recorders[action.Kind].SetShortcut(preferences.Shortcuts[action.Kind]);
            themes[preferences.Appearance].Checked = true;
            motion.Checked = preferences.AnimationsEnabled;
            transparency.Checked = preferences.TransparencyEnabled;
            language.SelectedItem = UiLanguage.Options.Single(option => option.Mode == preferences.Language);
            saveLocation.LoadPreferences(preferences);
            scrollingEditor.LoadOptions(scrollingOptions);
            ((SettingsDisclosure)Controls.Find("scrollingOptionsButton", true).Single()).Expanded = false;
            scrollingReveal.SetExpanded(false);
            ActiveControl = null;
        }
        finally { ResumeLayout(true); restoring = false; }
        if (refreshAppearance) RefreshSystemAppearance();
        scroller.AutoScrollPosition = Point.Empty;
        ShowFeedback(L("SaveHint"));
    }

    internal void SettleThemeSelection()
    {
        foreach (ThemeOption option in themes.Values) option.SettleSelection();
    }

    internal void RefreshSystemAppearance()
    {
        transparencyHint.Text = L(WindowBackdrop.IsSupported() ? "TransparencyHint" : "TransparencyUnavailable");
        transparency.AccessibleDescription = transparencyHint.Text;
        EndScrollingReveal();
        UiMotion.UserEnabled = draft.AnimationsEnabled;
        CapturePalette palette = CapturePalette.For(draft.Appearance);
        SettingsColors.Apply(this, palette, save, () => draft.TransparencyEnabled);
        scrollHost.ApplyPalette(palette);
        RefreshBarMaterials();
        foreach (ModeGlyph glyph in glyphs) { glyph.Ink = palette.Secondary; glyph.Invalidate(); }
    }

    private void RefreshBarMaterials()
    {
        CapturePalette palette = CapturePalette.For(draft.Appearance);
        bool enabled = draft.TransparencyEnabled && WindowBackdrop.IsSupported();
        headingBackdrop?.Apply(palette, enabled);
        footerBackdrop?.Apply(palette, enabled);
    }

    void IBackdropSnapshotOverlay.ApplySnapshotOverlay(BackdropSnapshot snapshot)
    {
        headingBackdrop.ApplySnapshot(snapshot, this);
        footerBackdrop.ApplySnapshot(snapshot, this);
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        // DrawToBitmap prints sibling windows in collection order. Reapply the two
        // foreground bars after the viewport so existing page-transition snapshots
        // have the same stacking as the live window.
        if (message.Msg == 0x317 && message.WParam != 0 && ((long)message.LParam & 16) != 0 && heading != null && footer != null)
        {
            SettingsBarBackdrop.PrintOverlay(heading, message.WParam);
            SettingsBarBackdrop.PrintOverlay(footer, message.WParam);
        }
    }

    private void ArrangePage()
    {
        // Minimized forms temporarily give docked pages a zero client size. Keep the
        // last usable layout instead of wrapping captions into a one-pixel column.
        if (initializing || arrangingOverlay || heading == null || footer == null || ClientSize.Width <= 0 || ClientSize.Height <= 0 || FindForm()?.WindowState == FormWindowState.Minimized) return;
        float scale = DeviceDpi / 96f;
        int gap = (int)(28 * scale);
        int width = Math.Max(1, ClientSize.Width);
        int readingWidth = Math.Max(1, Math.Min(width - gap * 2, (int)(760 * scale)));
        int left = (width - readingWidth) / 2;
        int right = width - readingWidth - left;
        Rectangle previousHeading = heading.Bounds, previousFooter = footer.Bounds;
        Padding previousHeadingPadding = heading.Padding, previousFooterPadding = footer.Padding;
        arrangingOverlay = true;
        try
        {
            scroller.Padding = new Padding(left, 0, right, 0);
            // Frost the full viewport width; keep each bar's contents in the original
            // reading column. Leave the existing scrollbar's hit area unobstructed.
            Control? track = scrollHost.Controls["settingsScrollBar"];
            int barWidth = Math.Max(1, width - (track is { Visible: true } ? track.Width : 0));
            int barRight = Math.Max(0, barWidth - left - readingWidth);
            heading.Padding = new Padding(left, gap, barRight, 0);
            heading.MaximumSize = new Size(barWidth, 0);
            heading.MinimumSize = new Size(barWidth, (int)(76 * scale) + gap);
            footer.Padding = new Padding(left, 0, barRight, gap);
            footer.MaximumSize = new Size(barWidth, 0);
            footer.MinimumSize = new Size(barWidth, 0);
            int headerHeight = heading.GetPreferredSize(new Size(barWidth, 0)).Height;
            int footerHeight = footer.GetPreferredSize(new Size(barWidth, 0)).Height;
            heading.Bounds = new Rectangle(0, 0, barWidth, headerHeight);
            footer.Bounds = new Rectangle(0, Math.Max(headerHeight, ClientSize.Height - footerHeight), barWidth, footerHeight);
            int top = heading.Bottom, bottom = Math.Max(0, ClientSize.Height - footer.Top);
            // Spacers belong to the scrollable canvas, not viewport.Padding, so AutoScroll
            // and the existing scrollbar retain exactly the same native scroll range.
            scrollCanvas.Padding = new Padding(0, top, 0, bottom);
            ((OverlayViewport)scroller).CoveredEdges = new Padding(0, top, 0, bottom);
            scrollHost.CoveredHeight = top + bottom;
        }
        finally { arrangingOverlay = false; }
        // Content height changes also run this layout. Keep static bar labels/buttons
        // cached unless their own geometry changed; their normal invalidations still
        // handle text, focus, hover, palette and animation feedback.
        if (heading.Bounds != previousHeading || heading.Padding != previousHeadingPadding) heading.Invalidate(true);
        if (footer.Bounds != previousFooter || footer.Padding != previousFooterPadding) footer.Invalidate(true);
    }

    private void PresentScrollFrame()
    {
        // Scroll moves native child windows but leaves their paints queued. Submit content
        // first, then its fixed glass bars, before presenting the thumb. No new invalidation:
        // preserve native scroll damage and avoid sampling the bars twice in one frame.
        Submit(scroller);
        Submit(heading);
        Submit(footer);
        static void Submit(Control control)
        {
            if (control.IsHandleCreated) RedrawWindow(control.Handle, 0, 0, 0x0100 | 0x0080); // RDW_UPDATENOW | RDW_ALLCHILDREN.
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(nint window, nint updateRect, nint updateRegion, uint flags);

    private sealed class BackgroundLayout : TableLayoutPanel, IBackdropBackground { }

    private sealed class OverlayViewport : SettingsScrollViewport, IBackdropBackground
    {
        internal Padding CoveredEdges { get; set; }
        protected override Point ScrollToControl(Control activeControl)
        {
            Point target = base.ScrollToControl(activeControl);
            Rectangle bounds = RectangleToClient(activeControl.RectangleToScreen(activeControl.ClientRectangle));
            bounds.Offset(target.X - AutoScrollPosition.X, target.Y - AutoScrollPosition.Y);
            int top = CoveredEdges.Top, bottom = ClientSize.Height - CoveredEdges.Bottom;
            if (bounds.Top < top || bounds.Height > bottom - top) target.Y += top - bounds.Top;
            else if (bounds.Bottom > bottom) target.Y -= bounds.Bottom - bottom;
            return target;
        }
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            EndScrollingReveal();
            headingBackdrop?.Dispose();
            footerBackdrop?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class SettingsGroup : TableLayoutPanel
{
    internal bool DrawDividers { get; set; }
    internal CapturePalette Palette { get; set; } = CapturePalette.Light;
    internal SettingsGroup() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaintBackground(PaintEventArgs e) => PaintSurface(e.Graphics);

    internal void PaintChildBackground(Control child, Graphics graphics)
    {
        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(-child.Left, -child.Top);
        PaintSurface(graphics);
        graphics.Restore(state);
    }

    private void PaintSurface(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Palette.Background);
        using GraphicsPath path = CaptureDrawing.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 16 * DeviceDpi / 96f);
        using SolidBrush brush = new(Palette.Surface);
        using Pen pen = new(Palette.Border);
        g.FillPath(brush, path);
        if (SystemInformation.HighContrast) g.DrawPath(pen, path);
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
    private readonly ButtonElevation elevation = new();
    internal void SettleSelection() => selection.SnapTo(Checked ? 1 : 0);
    internal ThemeOption()
    {
        Appearance = Appearance.Button;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        selection = new(this, Invalidate);
        hover = new(this, Invalidate);
        Disposed += (_, _) => elevation.Dispose();
    }
    protected override void OnMouseEnter(EventArgs e) { hover.SpringTo(1, ButtonElevation.ResponseMilliseconds); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover.SpringTo(0, ButtonElevation.ResponseMilliseconds); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); selection?.SpringTo(Checked ? 1 : 0); Invalidate(); }
    internal void PaintBackdrop(Graphics graphics)
    {
        float scale = DeviceDpi / 96f;
        graphics.Clear(Color.White);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        elevation.PaintShadow(graphics, ClientSize, scale, Palette.Background.GetBrightness() < .5f, Enabled ? hover.Value : 0, compact: true, unified: true);
        RectangleF bounds = ButtonElevation.CompactFaceBounds(ClientSize, scale);
        using GraphicsPath face = CaptureDrawing.Rounded(bounds, ButtonElevation.CompactRadius(bounds, scale));
        graphics.FillPath(Brushes.Black, face);
        if (Focused && ShowFocusCues)
        {
            using Pen focus = new(Color.Black, 1.75f * scale);
            graphics.DrawPath(focus, face);
        }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        CapturePalette p = Palette;
        float s = DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(p.Background);
        RectangleF face = ButtonElevation.CompactFaceBounds(ClientSize, s);
        elevation.PaintShadow(g, ClientSize, s, p.Background.GetBrightness() < .5f, Enabled ? hover.Value : 0, compact: true, unified: true);
        using GraphicsPath card = CaptureDrawing.Rounded(face, ButtonElevation.CompactRadius(face, s));
        using SolidBrush fill = new(CaptureDrawing.Blend(CaptureDrawing.Blend(p.Surface, ButtonElevation.Surface(p), hover.Value), p.Tint, selection.Value));
        g.FillPath(fill, card);
        if (Focused && ShowFocusCues || SystemInformation.HighContrast)
        {
            using Pen edge = new(Checked || Focused ? p.Accent : p.Border, 1.75f * s);
            g.DrawPath(edge, card);
        }
        RectangleF mini = new(face.Left + ButtonSpacing.Horizontal * s, (Height - 40 * s) / 2, 54 * s, 40 * s);
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
        // Outline the miniature only; the surrounding option card keeps its borderless style.
        g.DrawRectangle(line, mini.X + s / 2, mini.Y + s / 2, mini.Width - s, mini.Height - s);
        using SolidBrush accent = new(p.Accent);
        g.FillRectangle(accent, mini.X + 7 * s, mini.Y + 17 * s, 12 * s, 14 * s);
        g.DrawLine(line, mini.X + 25 * s, mini.Y + 19 * s, mini.Right - 6 * s, mini.Y + 19 * s);
        g.DrawLine(line, mini.X + 25 * s, mini.Y + 26 * s, mini.Right - 12 * s, mini.Y + 26 * s);
        int textLeft = (int)(mini.Right + 12 * s);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(textLeft, 0, Math.Max(1, (int)(face.Right - ButtonSpacing.Horizontal * s) - textLeft), Height), p.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | UiLanguage.TextDirection);
    }
}

internal sealed class ShortcutRecorder : Button
{
    internal Keys Shortcut { get; private set; }
    internal event EventHandler? Changed;
    internal CapturePalette Palette { get; set; } = CapturePalette.Light;
    private bool recording, pressed;
    private readonly UiTween hover;
    private readonly ButtonElevation elevation = new();
    private readonly ToolTip shortcutTip = new();

    internal ShortcutRecorder()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        hover = new(this, Invalidate);
    }
    internal void SetShortcut(Keys keys)
    {
        Shortcut = keys;
        recording = false;
        Text = keys == Keys.None ? L("Unset") : new HotkeyInfo(keys).ToString();
        AccessibleDescription = Text + L("RecorderDescription");
        shortcutTip.SetToolTip(this, AccessibleDescription);
        Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        recording = !recording;
        if (recording)
        {
            Text = L("PressShortcut");
            AccessibleDescription = Text;
            shortcutTip.SetToolTip(this, Text);
            Invalidate();
        }
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
    protected override void OnMouseEnter(EventArgs e) { hover.SpringTo(1, ButtonElevation.ResponseMilliseconds); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover.SpringTo(0, ButtonElevation.ResponseMilliseconds); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) { pressed = false; Invalidate(); } }
    public override Size GetPreferredSize(Size proposedSize) => new(base.GetPreferredSize(proposedSize).Width,
        Math.Max((int)Math.Round(ButtonSpacing.ToolHeight * DeviceDpi / 96f), TextRenderer.MeasureText("Ag", Font).Height + (int)Math.Round((12 + 2 * ButtonSpacing.Vertical) * DeviceDpi / 96f)));

    protected override void Dispose(bool disposing) { if (disposing) { shortcutTip.Dispose(); elevation.Dispose(); } base.Dispose(disposing); }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        CapturePalette p = Palette;
        float s = DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(p.Surface);
        RectangleF face = ButtonElevation.CompactFaceBounds(ClientSize, s);
        elevation.PaintShadow(g, ClientSize, s, p.Background.GetBrightness() < .5f, Enabled && !pressed ? hover.Value : 0, compact: true, unified: true);
        using GraphicsPath field = CaptureDrawing.Rounded(face, ButtonElevation.CompactRadius(face, s));
        using SolidBrush background = new(recording || pressed ? p.Tint : CaptureDrawing.Blend(p.Canvas, ButtonElevation.Surface(p), hover.Value));
        g.FillPath(background, field);
        if (recording || Focused && ShowFocusCues || SystemInformation.HighContrast)
        {
            using Pen edge = new(recording || Focused ? p.Accent : p.Border, 1.5f * s);
            g.DrawPath(edge, field);
        }
        Rectangle textBounds = Rectangle.Round(face);
        textBounds.Inflate(-(int)Math.Round(ButtonSpacing.Horizontal * s), 0);
        TextRenderer.DrawText(g, Text, Font, textBounds, !Enabled || Shortcut == Keys.None && !recording ? p.Secondary : recording ? p.Accent : p.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
    }
}

// Keep a native Button's keyboard and accessibility behavior without the general action button's minimum size.
internal sealed class ShortcutClearButton : Button
{
    internal CapturePalette Palette { get; set; } = CapturePalette.Light;
    private bool pressed;
    private readonly UiTween hover;
    private readonly ButtonElevation elevation = new();

    internal ShortcutClearButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Size = new Size(44, 44);
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        hover = new(this, Invalidate);
        Disposed += (_, _) => elevation.Dispose();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover.SpringTo(1, ButtonElevation.ResponseMilliseconds); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover.SpringTo(0, ButtonElevation.ResponseMilliseconds); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) { pressed = false; Invalidate(); } }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); pressed = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); pressed = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); pressed = false; hover?.SnapTo(0); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        CapturePalette p = Palette;
        Graphics g = e.Graphics;
        float s = DeviceDpi / 96f;
        g.Clear(Parent?.BackColor ?? p.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF face = ButtonElevation.CompactFaceBounds(ClientSize, s);
        elevation.PaintShadow(g, ClientSize, s, p.Background.GetBrightness() < .5f, Enabled && !pressed ? hover.Value : 0, compact: true, unified: true);
        using GraphicsPath shape = CaptureDrawing.Rounded(face, ButtonElevation.CompactRadius(face, s));
        if (Enabled && (hover.Value > 0 || pressed))
        {
            using SolidBrush fill = new(pressed ? p.Tint : CaptureDrawing.Blend(Parent?.BackColor ?? p.Surface, ButtonElevation.Surface(p), hover.Value));
            g.FillPath(fill, shape);
        }
        if (Enabled && (Focused && ShowFocusCues || SystemInformation.HighContrast))
        {
            using Pen focus = new(p.Accent, 1.5f * s);
            g.DrawPath(focus, shape);
        }
        Color ink = !Enabled && !SystemInformation.HighContrast ? CaptureDrawing.Blend(p.Surface, p.Secondary, .45f) :
            CaptureDrawing.Blend(p.Secondary, p.Text, pressed ? 1 : hover.Value);
        using Pen symbol = new(ink, 1.5f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float x = Width / 2f, y = Height / 2f, half = 3.5f * s;
        g.DrawLine(symbol, x - half, y - half, x + half, y + half);
        g.DrawLine(symbol, x - half, y + half, x + half, y - half);
    }
}
