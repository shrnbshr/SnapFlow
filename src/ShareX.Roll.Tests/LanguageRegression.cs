using ShareX.Roll;
using ShareX.Roll.Localization;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Drawing.Imaging;

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
            foreach ((string input, string expected) in new[] { ("zh-CN","zh-CN"), ("zh-SG","zh-CN"), ("zh-Hans","zh-CN"),
                ("zh-TW","zh-TW"), ("zh-HK","zh-TW"), ("zh-MO","zh-TW"), ("zh-Hant","zh-TW"), ("en-US","en"), ("fr-FR","fr"), ("ja-JP","ja-JP"),
                ("de-AT","de"), ("es-MX","es-MX"), ("fr-CA","fr"), ("pt-AO","pt-PT"), ("ar-SA","ar-YE"), ("th-TH","en") })
                Check(UiLanguage.Resolve(LanguageMode.System, CultureInfo.GetCultureInfo(input)).Name == expected, "Windows language mapping: " + input + " → " + expected);
            Check(UiLanguage.Resolve(LanguageMode.English, CultureInfo.GetCultureInfo("zh-CN")).Name == "en", "manual English overrides Chinese Windows");
            Check(UiLanguage.Resolve(LanguageMode.TraditionalChinese, CultureInfo.GetCultureInfo("en-US")).Name == "zh-TW", "manual traditional Chinese overrides English Windows");
            File.WriteAllText(path, "{\"Appearance\":0,\"Shortcuts\":{}}");
            Check(ApplicationPreferences.Load(path).Language == LanguageMode.System, "legacy preferences default to following Windows");
            ApplicationPreferences draft = original.Clone();
            draft.Language = LanguageMode.SimplifiedChinese;
            foreach (string kind in draft.Shortcuts.Keys.ToArray()) draft.Shortcuts[kind] = Keys.None;
            Set(main, "preferences", draft);
            Set(main, "preferencesPath", path);
            UiLanguage.Apply(draft.Language);
            Call(main, "ApplyWorkspaceLanguage");

            foreach (LanguageMode mode in new[] { LanguageMode.English, LanguageMode.TraditionalChinese, LanguageMode.SimplifiedChinese, LanguageMode.Japanese, LanguageMode.French })
            {
                Find<Button>(main, "settingsButton").PerformClick();
                ComboBox languages = Find<ComboBox>(main, "languageSelector");
                Check(languages.Items.Count == 25, "language selector offers Windows plus all 24 available languages");
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
                string expected = new System.ComponentModel.ComponentResourceManager(native.GetType()).GetString("btnCapture.Text", UiLanguage.Culture)!;
                Check(Find<Button>(native,"btnCapture").Text == expected, "existing capture resource selected: " + option.CultureName);
                Check(!string.IsNullOrWhiteSpace(UiText.L("Settings")), "main-window resource or English fallback: " + option.CultureName);
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
