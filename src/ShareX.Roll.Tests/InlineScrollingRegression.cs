using Newtonsoft.Json;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

internal static class InlineScrollingRegression
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private static string Json(object value) => JsonConvert.SerializeObject(value);
    private static void Render(MainForm main, string output, string name)
    {
        using Bitmap image = new(main.Width, main.Height);
        main.DrawToBitmap(image, new Rectangle(Point.Empty, main.Size));
        image.Save(Path.Combine(output, name + "-" + main.DeviceDpi + ".png"), ImageFormat.Png);
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/inline-scrolling");
        Directory.CreateDirectory(output);
        string oldPreferencesPath = Field<string>(main, "preferencesPath"), oldOptionsPath = Field<string>(main, "settingsPath");
        ApplicationPreferences oldPreferences = Field<ApplicationPreferences>(main, "preferences");
        ScrollingCaptureOptions oldOptions = Field<ScrollingCaptureOptions>(main, "scrollOptions");
        Size oldSize = main.Size;
        string preferencesPath = Path.Combine(output, "preferences.json"), optionsPath = Path.Combine(output, "options.json");
        ApplicationPreferences preferences = oldPreferences.Clone();
        preferences.AnimationsEnabled = false;
        preferences.Appearance = AppearanceMode.Light;
        preferences.Language = LanguageMode.SimplifiedChinese;
        foreach (string key in preferences.Shortcuts.Keys.ToArray()) preferences.Shortcuts[key] = Keys.None;
        ScrollingCaptureOptions original = new() { StartDelay = 400, ScrollDelay = 500, ScrollMethod = ScrollMethod.DownArrow, ScrollAmount = 3, AutoScrollTop = true, ShowRegion = false, AutoIgnoreBottomEdge = false, AutoUpload = false };
        try
        {
            Set(main, "preferencesPath", preferencesPath);
            Set(main, "settingsPath", optionsPath);
            Set(main, "preferences", preferences);
            Set(main, "scrollOptions", original);
            preferences.Save(preferencesPath);
            File.WriteAllText(optionsPath, Json(original));
            UiLanguage.Apply(preferences.Language);
            Call(main, "ApplyAppearance");
            int windows = Application.OpenForms.Count;
            Find<Button>(main, "settingsButton").PerformClick();
            SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
            SettingsDisclosure header = Find<SettingsDisclosure>(page, "scrollingOptionsButton");
            ScrollingOptionsEditor editor = Find<ScrollingOptionsEditor>(page, "scrollingOptionsEditor");
            Check(!header.Expanded && !editor.Visible && header.AccessibilityObject.State.HasFlag(AccessibleStates.Collapsed), "scrolling options start collapsed with an accessible state");
            header.AccessibilityObject.DoDefaultAction();
            Check(editor.Visible && header.AccessibilityObject.State.HasFlag(AccessibleStates.Expanded) && Application.OpenForms.Count == windows, "activating the disclosure expands in the original window");
            Check(Json(editor.ReadOptions()) == Json(original), "all eight existing capture options load unchanged");
            Find<NumericUpDown>(editor, "nudStartDelay").Value = 1200;
            Find<NumericUpDown>(editor, "nudScrollDelay").Value = 900;
            Find<NumericUpDown>(editor, "nudScrollAmount").Value = 5;
            Find<ComboBox>(editor, "cbScrollMethod").SelectedIndex = (int)ScrollMethod.PageDown;
            Check(!Find<NumericUpDown>(editor, "nudScrollAmount").Visible, "PageDown hides the unused scroll amount");
            Find<ComboBox>(editor, "cbScrollMethod").SelectedIndex = (int)ScrollMethod.ScrollMessage;
            Check(Find<NumericUpDown>(editor, "nudScrollAmount").Visible && Find<NumericUpDown>(editor, "nudScrollAmount").Value == 5, "switching back restores the retained scroll amount");
            Find<CheckBox>(editor, "cbAutoScrollTop").Checked = false;
            Find<CheckBox>(editor, "cbShowRegion").Checked = true;
            Find<CheckBox>(editor, "cbAutoIgnoreBottomEdge").Checked = true;
            Find<CheckBox>(editor, "cbAutoUpload").Checked = true;
            string edited = Json(editor.ReadOptions());
            header.PerformClick();
            header.PerformClick();
            Check(Json(editor.ReadOptions()) == edited && Json(Field<ScrollingCaptureOptions>(main, "scrollOptions")) == Json(original), "collapse preserves draft values without changing capture options");
            Find<Button>(page, "cancelSettingsButton").PerformClick();
            Check(Json(Field<ScrollingCaptureOptions>(main, "scrollOptions")) == Json(original) && Json(JsonConvert.DeserializeObject<ScrollingCaptureOptions>(File.ReadAllText(optionsPath))!) == Json(original), "cancel leaves both active and saved scrolling options unchanged");

            Find<Button>(main, "settingsButton").PerformClick();
            page = Find<SettingsPage>(main, "SettingsPage");
            header = Find<SettingsDisclosure>(page, "scrollingOptionsButton");
            header.PerformClick();
            editor = Find<ScrollingOptionsEditor>(page, "scrollingOptionsEditor");
            Find<NumericUpDown>(editor, "nudStartDelay").Text = "1600";
            string saved = Json(editor.ReadOptions());
            Check(editor.ReadOptions().StartDelay == 1600, "save reads a number still being typed without requiring a focus change");
            Find<Button>(page, "saveSettingsButton").PerformClick();
            Check(Field<SettingsPage?>(main, "settingsPage") == null && Json(Field<ScrollingCaptureOptions>(main, "scrollOptions")) == saved, "save commits scrolling options and returns to capture");
            Check(Json(JsonConvert.DeserializeObject<ScrollingCaptureOptions>(File.ReadAllText(optionsPath))!) == saved, "saved scrolling options survive disk reload");

            Find<Button>(main, "settingsButton").PerformClick();
            page = Find<SettingsPage>(main, "SettingsPage");
            header = Find<SettingsDisclosure>(page, "scrollingOptionsButton");
            header.PerformClick();
            editor = Find<ScrollingOptionsEditor>(page, "scrollingOptionsEditor");
            Check(Json(editor.ReadOptions()) == saved, "reopening settings retains the saved options");
            Find<Button>(page, "resetSettingsButton").PerformClick();
            Check(Json(editor.ReadOptions()) == Json(new ScrollingCaptureOptions()) && Json(Field<ScrollingCaptureOptions>(main, "scrollOptions")) == saved, "restore defaults changes only the draft");
            Find<Button>(page, "cancelSettingsButton").PerformClick();

            byte[] beforePreferences = File.ReadAllBytes(preferencesPath);
            ApplicationPreferences failedPreferences = Field<ApplicationPreferences>(main, "preferences").Clone();
            failedPreferences.Appearance = AppearanceMode.Dark;
            Set(main, "settingsPath", output); // An existing directory rejects the final file replacement.
            Check(Call(main, "TrySaveSettings", failedPreferences, new ScrollingCaptureOptions()) is string,
                "scrolling write failure returns an inline error");
            Check(File.ReadAllBytes(preferencesPath).SequenceEqual(beforePreferences) && Json(Field<ScrollingCaptureOptions>(main, "scrollOptions")) == saved, "failed save restores preferences and leaves capture options unchanged");
            Set(main, "settingsPath", optionsPath);

            foreach (LanguageMode language in new[] { LanguageMode.SimplifiedChinese, LanguageMode.English })
            foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                ApplicationPreferences visual = Field<ApplicationPreferences>(main, "preferences").Clone();
                visual.Language = language;
                visual.Appearance = mode;
                Set(main, "preferences", visual);
                UiLanguage.Apply(language);
                Call(main, "ApplyAppearance");
                main.Size = oldSize;
                Find<Button>(main, "settingsButton").PerformClick();
                page = Find<SettingsPage>(main, "SettingsPage");
                header = Find<SettingsDisclosure>(page, "scrollingOptionsButton");
                header.PerformClick();
                editor = Find<ScrollingOptionsEditor>(page, "scrollingOptionsEditor");
                Panel viewport = Field<Panel>(page, "scroller");
                Control row = Find<Control>(page, "scrollingOptionsRow");
                foreach (bool minimum in new[] { false, true })
                {
                    if (minimum) main.Size = main.MinimumSize;
                    await Task.Delay(60);
                    viewport.AutoScrollPosition = new Point(0, row.Top);
                    Check(editor.Width <= viewport.ClientSize.Width && !viewport.HorizontalScroll.Visible, $"{language}/{mode}/{minimum}: expanded parameters fit without horizontal scrolling");
                    foreach (string name in new[] { "nudStartDelayUnit", "nudScrollDelayUnit", "nudScrollAmountUnit" })
                    {
                        Label unit = Find<Label>(editor, name);
                        Check(unit.PreferredWidth <= unit.Width, "numeric unit label is fully visible: " + name);
                    }
                    Control last = Find<Control>(editor, "cbAutoUpload");
                    viewport.ScrollControlIntoView(last);
                    Check(viewport.RectangleToScreen(viewport.ClientRectangle).Contains(last.RectangleToScreen(last.ClientRectangle)), "last scrolling option remains reachable");
                    Check(page.RectangleToScreen(page.ClientRectangle).Contains(Find<Button>(page, "saveSettingsButton").RectangleToScreen(Find<Button>(page, "saveSettingsButton").ClientRectangle)), "save footer stays visible while expanded");
                    viewport.AutoScrollPosition = new Point(0, row.Top);
                    Render(main, output, $"expanded-{language}-{mode}-{(minimum ? "minimum" : "normal")}");
                }
                Find<Button>(page, "backToCaptureButton").PerformClick();
            }
            Check(Application.OpenForms.Count == windows, "inline scrolling settings never create another window");
        }
        finally
        {
            Call(main, "CloseSettings");
            Set(main, "preferencesPath", oldPreferencesPath);
            Set(main, "settingsPath", oldOptionsPath);
            Set(main, "preferences", oldPreferences);
            Set(main, "scrollOptions", oldOptions);
            UiLanguage.Apply(oldPreferences.Language);
            Call(main, "ApplyAppearance");
            Call(main, "RegisterCaptureHotkeys");
            main.Size = oldSize;
        }
    }
}
