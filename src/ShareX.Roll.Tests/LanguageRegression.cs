using ShareX.Roll;
using ShareX.Roll.Localization;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Drawing.Imaging;
using System.Collections;
using System.Resources;

internal static class LanguageRegression
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/language-settings");
        Directory.CreateDirectory(output);
        string path = Path.Combine(output, "test-preferences.json");
        string oldPath = Field<string>(main, "preferencesPath");
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        CultureInfo regionalCulture = CultureInfo.CurrentCulture;
        Size initialSize = main.Size;
        try
        {
            // Do not accept a non-empty English fallback as a completed translation.
            ResourceSet englishResources = Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
            foreach (LanguageOption option in UiLanguage.Options.Where(option => option.Mode != LanguageMode.System))
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(option.CultureName);
                ResourceSet? localized = Strings.ResourceManager.GetResourceSet(option.Mode == LanguageMode.English ? CultureInfo.InvariantCulture : culture, true, false);
                Check(localized != null, "dedicated main-window resource catalog: " + culture.Name);
                foreach (DictionaryEntry entry in englishResources)
                {
                    string key = (string)entry.Key;
                    string? value = localized!.GetString(key);
                    if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Missing direct translation: " + culture.Name + "/" + key);
                    if (value.Contains('{')) _ = string.Format(culture, value, 123, 456);
                }
                Check(localized!.Cast<DictionaryEntry>().Count() == englishResources.Cast<DictionaryEntry>().Count(), "all main-window strings present without fallback: " + culture.Name);
            }
            foreach ((string input, string expected) in new[] { ("zh-CN","zh-CN"), ("zh-SG","zh-CN"), ("zh-Hans","zh-CN"),
                ("zh-TW","zh-HK"), ("zh-HK","zh-HK"), ("zh-MO","zh-HK"), ("zh-Hant","zh-HK"), ("en-US","en"), ("fr-FR","en"), ("ja-JP","en"),
                ("de-AT","en"), ("es-MX","en"), ("fr-CA","en"), ("pt-AO","en"), ("ar-SA","en"), ("th-TH","en") })
                Check(UiLanguage.Resolve(LanguageMode.System, CultureInfo.GetCultureInfo(input)).Name == expected, "Windows language mapping: " + input + " → " + expected);
            Check(UiLanguage.Resolve(LanguageMode.English, CultureInfo.GetCultureInfo("zh-CN")).Name == "en", "manual English overrides Chinese Windows");
            Check(UiLanguage.Resolve(LanguageMode.TraditionalChinese, CultureInfo.GetCultureInfo("en-US")).Name == "zh-HK", "manual Hong Kong Traditional Chinese overrides English Windows");
            Check(UiLanguage.Options.Single(option => option.Mode == LanguageMode.TraditionalChinese).ToString() == "繁體中文（香港）", "traditional Chinese choice identifies Hong Kong");
            File.WriteAllText(path, "{\"Appearance\":0,\"Shortcuts\":{}}");
            Check(ApplicationPreferences.Load(path).Language == LanguageMode.System, "legacy preferences default to following Windows");
            foreach (int retired in Enumerable.Range(4, 21))
            {
                ApplicationPreferences legacy = original.Clone();
                legacy.Language = (LanguageMode)retired;
                legacy.Save(path);
                ApplicationPreferences migrated = ApplicationPreferences.Load(path);
                Check(migrated.Language == LanguageMode.System && migrated.Appearance == legacy.Appearance &&
                    migrated.AnimationsEnabled == legacy.AnimationsEnabled && legacy.Shortcuts.All(pair => migrated.Shortcuts[pair.Key] == pair.Value),
                    "retired language migrates without losing preferences: " + retired);
            }
            ApplicationPreferences draft = original.Clone();
            draft.Language = LanguageMode.SimplifiedChinese;
            foreach (string kind in draft.Shortcuts.Keys.ToArray()) draft.Shortcuts[kind] = Keys.None;
            Set(main, "preferences", draft);
            Set(main, "preferencesPath", path);
            UiLanguage.Apply(draft.Language);
            Call(main, "ApplyWorkspaceLanguage");

            foreach (LanguageMode mode in UiLanguage.Options.Where(option => option.Mode != LanguageMode.System).Select(option => option.Mode))
            {
                Find<Button>(main, "settingsButton").PerformClick();
                ComboBox languages = Find<ComboBox>(main, "languageSelector");
                Check(languages.Items.Count == 4, "language selector offers Windows plus Chinese, Hong Kong Traditional Chinese and English only");
                LanguageMode previous = Field<ApplicationPreferences>(main, "preferences").Language;
                languages.SelectedIndex = (int)mode;
                Check(Field<ApplicationPreferences>(main, "preferences").Language == previous, "selection remains a draft until saved");
                Find<Button>(main, "saveSettingsButton").PerformClick();
                await Task.Delay(260);
                Check(ApplicationPreferences.Load(path).Language == mode, "saved language persists: " + mode);
                Check(Find<Button>(main, "settingsButton").Text == UiText.L("Settings"), "main window updates immediately after save: " + mode);
                Check(Find<Button>(main, "regionButton").Text == UiText.L("Region"), "capture action labels change: " + mode);
                using (Bitmap bitmap = new(main.Width, main.Height))
                {
                    main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, main.Size));
                    bitmap.Save(Path.Combine(output, $"main-{mode}-{main.DeviceDpi}.png"), ImageFormat.Png);
                }
                Find<Button>(main, "settingsButton").PerformClick();
                Check(Find<Button>(main, "saveSettingsButton").Text == UiText.L("SaveReturn"), "settings uses the selected language: " + mode);
                Check(Find<ComboBox>(main, "languageSelector").SelectedIndex == (int)mode, "reopened selector retains the saved choice");
                Find<Button>(main, "scrollingOptionsButton").PerformClick();
                await Task.Delay(260);
                Control scrolling = Find<Control>(main, "scrollingOptionsEditor");
                Check(scrolling.Visible && Find<CheckBox>(scrolling, "cbAutoUpload").AccessibleName == UiText.L("ScrollAutoSave"),
                    "inline scrolling options use the selected language: " + mode);
                Find<Button>(main, "scrollingOptionsButton").PerformClick();
                await Task.Delay(260);
                using (Bitmap bitmap = new(main.Width, main.Height))
                {
                    main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, main.Size));
                    bitmap.Save(Path.Combine(output, $"settings-{mode}-{main.DeviceDpi}.png"), ImageFormat.Png);
                }
                SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
                Panel viewport = Field<Panel>(page, "scroller");
                Size normalSize = main.Size;
                main.Size = main.MinimumSize;
                await Task.Delay(60);
                viewport.AutoScrollPosition = new Point(0, viewport.DisplayRectangle.Height);
                Check(!viewport.HorizontalScroll.Visible, "about section wraps at minimum window width: " + mode);
                Control origin = Find<Control>(page, "aboutOrigin");
                Check(origin.Height >= origin.GetPreferredSize(new Size(origin.Width, 0)).Height,
                    "origin description is fully laid out: " + mode);
                foreach (string buttonName in new[] { "saveSettingsButton", "cancelSettingsButton", "resetSettingsButton", "backToCaptureButton" })
                {
                    Button button = Find<Button>(page, buttonName);
                    Check(button.Width >= TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.SingleLine).Width,
                        "translated button text fits: " + mode + "/" + buttonName);
                }
                Check(Find<Button>(page, "saveSettingsButton").RectangleToScreen(Find<Button>(page, "saveSettingsButton").ClientRectangle).Right <= main.RectangleToScreen(main.ClientRectangle).Right,
                    "translated footer stays inside minimum window: " + mode);
                foreach (string rowName in new[] { "motionRow", "languageRow" })
                {
                    Control row = Find<Control>(page, rowName);
                    Control caption = row.Controls[0];
                    Check(caption.Controls.OfType<Label>().All(label => label.Bottom <= caption.ClientSize.Height),
                        "translated captions wrap without vertical clipping: " + mode + "/" + rowName);
                }
                Check(origin.RightToLeft == (UiLanguage.Culture.TextInfo.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No),
                    "text direction follows language: " + mode);
                using (Bitmap bitmap = new(main.Width, main.Height))
                {
                    main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, main.Size));
                    bitmap.Save(Path.Combine(output, $"about-minimum-{mode}-{main.DeviceDpi}.png"), ImageFormat.Png);
                }
                main.Size = normalSize;
                Find<ComboBox>(main, "languageSelector").SelectedIndex = (int)LanguageMode.System;
                Find<Button>(main, "backToCaptureButton").PerformClick();
                Check(ApplicationPreferences.Load(path).Language == mode && UiLanguage.Culture.Name == UiLanguage.Resolve(mode, CultureInfo.GetCultureInfo("en-US")).Name,
                    "cancel discards the language draft: " + mode);
                using Bitmap source = new(300, 200);
                using ShareX.ScreenCaptureLib.RegionCaptureForm editor = ScreenshotEditor.Create(source, CapturePalette.Light);
                editor.Show(main);
                await Task.Delay(160);
                ToolStrip actions = EditorRegression.Actions(editor);
                Check(actions.Items["editorApply"]!.Text == UiText.L("ApplyPreview") && actions.Items["editorSaveAs"]!.Text == UiText.L("ExportImage"),
                    "editor output actions use the selected language: " + mode);
                Check(actions.Items["editorApply"]!.Width >= TextRenderer.MeasureText(UiText.L("ApplyPreview"), actions.Font).Width,
                    "editor apply action fits translated text: " + mode);
                editor.Close();
            }
            ApplicationPreferences english = ApplicationPreferences.Load(path);
            english.Language = LanguageMode.English;
            english.Save(path);
            UiLanguage.Initialize(path);
            using (MainForm restarted = new())
                Check(Find<Button>(restarted, "settingsButton").Text == "Settings", "startup applies the saved language before constructing the first window");
            Check(CultureInfo.CurrentCulture.Equals(regionalCulture), "changing UI language preserves regional number/date formats");

            foreach (LanguageOption option in UiLanguage.Options.Where(option => option.Mode != LanguageMode.System))
            {
                UiLanguage.Apply(option.Mode);
                ApplicationPreferences selected = original.Clone();
                selected.Language = option.Mode;
                selected.Save(path);
                Check(ApplicationPreferences.Load(path).Language == option.Mode, "language persistence: " + option.CultureName);
                using ShareX.ScreenCaptureLib.ScrollingCaptureForm native = (ShareX.ScreenCaptureLib.ScrollingCaptureForm)Activator.CreateInstance(
                    typeof(ShareX.ScreenCaptureLib.ScrollingCaptureForm), BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new object[] { new ShareX.ScreenCaptureLib.ScrollingCaptureOptions() }, null)!;
                var nativeResources = new System.ComponentModel.ComponentResourceManager(native.GetType());
                Check(nativeResources.GetResourceSet(option.Mode == LanguageMode.English ? CultureInfo.InvariantCulture : UiLanguage.Culture, true, false) != null,
                    "dedicated capture resource catalog without fallback: " + option.CultureName);
                string expected = nativeResources.GetString("btnCapture.Text", UiLanguage.Culture)!;
                Check(Find<Button>(native,"btnCapture").Text == expected, "existing capture resource selected: " + option.CultureName);
                Check(!string.IsNullOrWhiteSpace(UiText.L("Settings")), "localized main-window resource selected: " + option.CultureName);
            }
            UiLanguage.Apply(Field<ApplicationPreferences>(main,"preferences").Language);

            Find<Button>(main, "settingsButton").PerformClick();
            Find<Button>(main, "resetSettingsButton").PerformClick();
            Check(Find<ComboBox>(main, "languageSelector").SelectedIndex == 0, "restore defaults selects following Windows");
            Find<Button>(main, "cancelSettingsButton").PerformClick();
            main.Size = main.MinimumSize;
            Find<Button>(main, "settingsButton").PerformClick();
            Check(Find<Button>(main, "saveSettingsButton").RectangleToScreen(Find<Button>(main, "saveSettingsButton").ClientRectangle).Bottom <= main.RectangleToScreen(main.ClientRectangle).Bottom,
                "save stays reachable when language row requires scrolling at minimum size");
            Find<Button>(main, "backToCaptureButton").PerformClick();
        }
        finally
        {
            Call(main, "CloseSettings");
            Set(main, "preferencesPath", oldPath);
            Set(main, "preferences", original);
            UiLanguage.Apply(original.Language);
            Call(main, "ApplyWorkspaceLanguage");
            Call(main, "ApplyAppearance");
            Call(main, "RegisterCaptureHotkeys");
            main.Size = initialSize;
        }
    }
}
