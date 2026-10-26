using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Reflection;

internal static class SettingsPageReuseRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Call(MainForm main, string name) => typeof(MainForm).GetMethod(name, Private)!.Invoke(main, null);
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    internal static async Task RunAsync(MainForm main)
    {
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        ScrollingCaptureOptions originalOptions = Field<ScrollingCaptureOptions>(main, "scrollOptions");
        try
        {
            SettingsPage prepared = Field<SettingsPage>(main, "cachedSettingsPage");
            Check(!prepared.Visible && prepared.IsHandleCreated && Field<SettingsPage?>(main, "settingsPage") == null,
                "startup prepares a native settings page without making it the active page");
            nint handle = prepared.Handle;
            Call(main, "OpenSettings"); await Task.Delay(280);
            SettingsPage page = Field<SettingsPage>(main, "settingsPage");
            Check(ReferenceEquals(page, prepared) && page.Handle == handle, "first entry reuses the prepared page and its native handle");
            if (UiMotion.Enabled)
            {
                MethodInfo snapshot = typeof(MainForm).GetMethod("SettingsSnapshot", Private)!;
                using Bitmap? same = (Bitmap?)snapshot.Invoke(main, new object[] { page });
                Check(same != null, "an unchanged restored page can reuse its transition image");
                ShortcutRecorder recorder = Find<ShortcutRecorder>(page, "regionShortcut");
                string text = recorder.Text;
                recorder.Text = "changed shortcut label";
                using Bitmap? changed = (Bitmap?)snapshot.Invoke(main, new object[] { page });
                Check(changed == null, "a changed control invalidates the cached transition image");
                recorder.Text = text;
            }
            Find<RadioButton>(page, original.Appearance == AppearanceMode.Dark ? "themeLight" : "themeDark").Checked = true;
            Find<CheckBox>(page, "transparencyToggle").Checked = !original.TransparencyEnabled;
            Find<CheckBox>(page, "animationsToggle").Checked = !original.AnimationsEnabled;
            Find<ComboBox>(page, "languageSelector").SelectedItem = UiLanguage.Options.Single(option => option.Mode == LanguageMode.English);
            Find<CheckBox>(page, "defaultSaveFolderToggle").Checked = !original.UseDefaultSaveFolder;
            Find<ShortcutRecorder>(page, "regionShortcut").SetShortcut(Keys.None);
            Find<Button>(page, "scrollingOptionsButton").PerformClick();
            Find<NumericUpDown>(page, "nudStartDelay").Value = 2000;
            Call(main, "CloseSettings");
            Check(!page.Visible && !page.IsDisposed && Field<SettingsPage?>(main, "settingsPage") == null,
                "return hides the reusable page and leaves the capture workspace active");
            Call(main, "OpenSettings"); await Task.Delay(250);
            Check(ReferenceEquals(Field<SettingsPage>(main, "settingsPage"), prepared) && prepared.Handle == handle,
                "a subsequent visit reuses the same page and native handle");
            ApplicationPreferences draft = Field<ApplicationPreferences>(page, "draft");
            Check(draft.Appearance == original.Appearance && draft.TransparencyEnabled == original.TransparencyEnabled &&
                draft.AnimationsEnabled == original.AnimationsEnabled && draft.Language == original.Language &&
                draft.UseDefaultSaveFolder == original.UseDefaultSaveFolder && draft.DefaultSaveFolder == original.DefaultSaveFolder &&
                original.Shortcuts.All(pair => draft.Shortcuts[pair.Key] == pair.Value),
                "reopening discards every unsaved preference and shortcut change");
            Check(!Find<SettingsDisclosure>(page, "scrollingOptionsButton").Expanded &&
                Find<NumericUpDown>(page, "nudStartDelay").Value == originalOptions.StartDelay,
                "reopening restores collapsed scrolling options and discards their draft values");
            Check(ReferenceEquals(Field<ApplicationPreferences>(main, "preferences"), original),
                "draft restoration does not replace or mutate the saved preference object");
            Call(main, "CloseSettings");
            ScrollingCaptureOptions updated = new() { StartDelay = 1300, ScrollDelay = 700, ScrollAmount = 3 };
            typeof(MainForm).GetField("scrollOptions", Private)!.SetValue(main, updated);
            Call(main, "OpenSettings");
            Check(Find<NumericUpDown>(page, "nudStartDelay").Value == updated.StartDelay &&
                Find<NumericUpDown>(page, "nudScrollDelay").Value == updated.ScrollDelay,
                "cached controls reload current capture options instead of retaining a startup copy");
        }
        finally
        {
            Call(main, "CloseSettings"); typeof(MainForm).GetField("preferences", Private)!.SetValue(main, original);
            typeof(MainForm).GetField("scrollOptions", Private)!.SetValue(main, originalOptions);
            Call(main, "ApplyAppearance");
        }
    }
}
