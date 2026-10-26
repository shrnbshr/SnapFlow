using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.Reflection;
using System.IO;

internal static class SettingsRegression
{
    private static readonly string Output = Path.GetFullPath("artifacts/hig-refresh");
    private static object? Call(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static FieldInfo FieldInfo(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static T Field<T>(object target, string name) => (T)FieldInfo(target, name).GetValue(target)!;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private static T Find<T>(Control parent, string name) where T : Control => (T)parent.Controls.Find(name, true).Single();

    internal static async Task RunAsync(MainForm main)
    {
        Directory.CreateDirectory(Output);
        string path = Field<string>(main, "preferencesPath");
        byte[]? previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences").Clone();
        try
        {
            Check(Find<Button>(main, "settingsButton").Visible, "settings entry is visible in main window");
            int windowCount = Application.OpenForms.Count;
            IntPtr mainHandle = main.Handle;
            Find<Button>(main, "settingsButton").PerformClick();
            SettingsPage initialPage = Find<SettingsPage>(main, "SettingsPage");
            Check(Application.OpenForms.Count == windowCount && main.Handle == mainHandle,
                "settings opens inside the same main window without a popup");
            Check(!Field<TableLayoutPanel>(main, "workspace").Visible, "capture workspace is retained behind the settings page");
            SettingsDisclosure disclosure = Find<SettingsDisclosure>(initialPage, "scrollingOptionsButton");
            Check(!disclosure.Expanded, "scrolling settings start collapsed");
            disclosure.PerformClick();
            Check(disclosure.Expanded && Find<ScrollingOptionsEditor>(initialPage, "scrollingOptionsEditor").Visible
                && Application.OpenForms.Count == windowCount, "scrolling parameters expand inline without another window");
            disclosure.PerformClick();
            await Task.Delay(380);
            Check(!disclosure.Expanded && !Find<ScrollingOptionsEditor>(initialPage, "scrollingOptionsEditor").Visible,
                "scrolling parameters collapse again");
            Find<RadioButton>(initialPage, "themeDark").Checked = true;
            Find<Button>(initialPage, "cancelSettingsButton").PerformClick();
            Check(Field<SettingsPage?>(main, "settingsPage") == null && Field<TableLayoutPanel>(main, "workspace").Visible,
                "cancel returns to the preserved capture workspace");
            Check(Field<ApplicationPreferences>(main, "preferences").Appearance == original.Appearance,
                "cancel discards draft theme and restores hotkeys");

            Call(main, "ReleaseCaptureHotkeys");
            ApplicationPreferences candidate = new() { Appearance = AppearanceMode.Dark };
            bool Available(params Keys[] combinations)
            {
                using HotkeyForm probe = new();
                List<HotkeyInfo> registrations = new();
                try
                {
                    foreach (Keys combination in combinations)
                    {
                        HotkeyInfo hotkey = new(combination);
                        probe.RegisterHotkey(hotkey);
                        registrations.Add(hotkey);
                        if (hotkey.Status != HotkeyStatus.Registered) return false;
                    }
                    return true;
                }
                finally { foreach (HotkeyInfo hotkey in registrations.Where(k => k.Status == HotkeyStatus.Registered)) probe.UnregisterHotkey(hotkey); }
            }
            Keys[] free = Enumerable.Range((int)Keys.F13, 12).Select(k => (Keys)k)
                .Where(k => Available(Keys.Control | Keys.Shift | k, Keys.Control | Keys.Alt | k)).Take(3).ToArray();
            Check(free.Length == 3, "found unoccupied function keys for isolated regression");
            candidate.Shortcuts["region"] = Keys.Control | Keys.Shift | free[0];
            candidate.Shortcuts["full"] = Keys.Control | Keys.Alt | free[0];
            candidate.Shortcuts["scroll"] = Keys.Control | Keys.Alt | free[2];
            ApplicationPreferences duplicate = candidate.Clone();
            duplicate.Shortcuts["scroll"] = duplicate.Shortcuts["region"];
            Check(((string?)Call(main, "TrySavePreferences", duplicate))?.Contains("相同") == true,
                "duplicate combinations are rejected");

            using (HotkeyForm other = new())
            {
                HotkeyInfo occupied = new(candidate.Shortcuts["region"]);
                other.RegisterHotkey(occupied);
                Check(occupied.Status == HotkeyStatus.Registered, "independent window reserves a shortcut for conflict test");
                Check(((string?)Call(main, "TrySavePreferences", candidate))?.Contains(ShareX.Roll.Localization.UiText.L("Region")) == true,
                    "OS conflict reports the affected action");
                Check(Field<List<HotkeyInfo>>(main, "hotkeys").Count == 0, "failed validation releases temporary registrations");
                Check(Field<ApplicationPreferences>(main, "preferences").Appearance == original.Appearance,
                    "conflict does not overwrite original preferences");
                other.UnregisterHotkey(occupied);
            }

            // Save failure must leave both the in-memory preferences and prior file intact.
            FieldInfo(main, "preferencesPath").SetValue(main, Path.Combine(Output, "missing-" + Guid.NewGuid().ToString("N"), "preferences.json"));
            Check(((string?)Call(main, "TrySavePreferences", candidate))?.Contains("未能保存") == true, "unwritable location is reported without committing settings");
            FieldInfo(main, "preferencesPath").SetValue(main, path);
            Check(Call(main, "TrySavePreferences", candidate) == null, "available custom hotkeys save successfully");
            ApplicationPreferences loaded = ApplicationPreferences.Load(path);
            Check(loaded.Appearance == AppearanceMode.Dark && loaded.Shortcuts.SequenceEqual(candidate.Shortcuts),
                "saved shortcuts and appearance survive reload");
            Call(main, "RegisterCaptureHotkeys");
            Call(main, "ApplyAppearance");
            var routes = Field<Dictionary<ushort, string>>(main, "hotkeyActions");
            Check(routes.Values.Distinct().Count() == 3 && !routes.Values.Contains("window"), "three custom combinations map to the merged actions");
            Check(Field<List<HotkeyInfo>>(main, "hotkeys").Count(k => k.KeyCode == free[0] && k.Status == HotkeyStatus.Registered) == 2,
                "same base key with different modifiers registers independently");

            main.TopMost = true;
            main.ForceActivate();
            await Task.Delay(1200);
            Check(InputHelpers.SendKeyPressModifiers((VirtualKeyCode)free[0], VirtualKeyCode.CONTROL, VirtualKeyCode.MENU),
                "custom full-screen shortcut sent through Windows input");
            await Task.Delay(900);
            Bitmap? result = Field<Bitmap?>(main, "result");
            Check(result != null && main.Visible && !Application.OpenForms.OfType<ScrollingCaptureForm>().Any(),
                "custom function-key shortcut captures full screen instead of dispatching to scrolling capture");
            result!.Save(Path.Combine(Output, "custom-hotkey-full.png"), ImageFormat.Png);
            main.TopMost = false;

            using (MainForm restarted = new())
            {
                Check(Field<ApplicationPreferences>(restarted, "preferences").Shortcuts["full"] == candidate.Shortcuts["full"]
                    && restarted.BackColor == CapturePalette.For(AppearanceMode.Dark).Background,
                    "new main window restores saved custom hotkeys and dark appearance");
            }

            foreach (AppearanceMode mode in Enum.GetValues<AppearanceMode>())
            {
                Find<Button>(main, "settingsButton").PerformClick();
                SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
                Find<RadioButton>(page, "theme" + mode).Checked = true;
                await Task.Delay(280);
                using Bitmap bitmap = new(main.Width, main.Height);
                main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, main.Size));
                bitmap.Save(Path.Combine(Output, "settings-" + mode + "-" + page.DeviceDpi + ".png"), ImageFormat.Png);
                Check(Application.OpenForms.Count == windowCount && page.Size == main.ClientSize,
                    mode + " page fills the original main window");
                Check(Find<RadioButton>(page, "theme" + mode).Checked && main.BackColor == CapturePalette.For(mode).Background,
                    "selected appearance previews across the main window");
                ShortcutRecorder recorder = Find<ShortcutRecorder>(page, "regionShortcut");
                Check(recorder.Height >= 52 * page.DeviceDpi / 96 && recorder.Height <= 56 * page.DeviceDpi / 96,
                    "shortcut controls preserve compact vertical padding at current DPI");
                Check(Find<Button>(page, "saveSettingsButton").RectangleToScreen(Find<Button>(page, "saveSettingsButton").ClientRectangle).Bottom
                    <= page.RectangleToScreen(page.ClientRectangle).Bottom,
                    "fixed save / cancel footer stays within the main window");
                // Exercise recorder through native command handling, including cancellation and validation.
                Keys before = recorder.Shortcut;
                recorder.PerformClick();
                typeof(ShortcutRecorder).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(recorder, [new Message(), Keys.Escape]);
                Check(recorder.Shortcut == before, "Escape cancels shortcut recording");
                recorder.PerformClick();
                typeof(ShortcutRecorder).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(recorder, [new Message(), Keys.Control | Keys.Shift | Keys.F9]);
                Check(recorder.Shortcut == (Keys.Control | Keys.Shift | Keys.F9), "recorder accepts a new combination");
                Find<Button>(page, "regionClear").PerformClick();
                Check(recorder.Shortcut == Keys.None, "clear disables a shortcut");
                Check(Find<Button>(page, "regionClear").AccessibleName!.Contains(ShareX.Roll.Localization.UiText.L("Region")),
                    "clear icon retains its descriptive accessible name");
                Find<Button>(page, "resetSettingsButton").PerformClick();
                Check(recorder.Shortcut == (Keys.Control | Keys.Shift | Keys.A), "restore defaults updates recorder");
                Find<Button>(page, "cancelSettingsButton").PerformClick();
                Check(Field<SettingsPage?>(main, "settingsPage") == null && main.Handle == mainHandle,
                    "back returns without replacing the main window");
            }
            Check(ReferenceEquals(result, Field<Bitmap?>(main, "result")), "settings navigation preserves the existing captured image");

            Call(main, "ReleaseCaptureHotkeys");
            {
                Find<Button>(main, "settingsButton").PerformClick();
                SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
                ShortcutRecorder recorder = Find<ShortcutRecorder>(page, "regionShortcut");
                recorder.Focus();
                recorder.PerformClick();
                await Task.Delay(100);
                InputHelpers.SendKeyPressModifiers((VirtualKeyCode)free[0], VirtualKeyCode.CONTROL, VirtualKeyCode.MENU);
                await Task.Delay(120);
                Check(recorder.Shortcut == (Keys.Control | Keys.Alt | free[0]), "real keyboard input records a replacement shortcut");
                Find<Button>(page, "saveSettingsButton").PerformClick();
                Check(page.Visible && Find<Label>(page, "settingsFeedback").Text.Contains("相同"),
                    "save displays duplicate validation inline and keeps settings page open");
                recorder.SetShortcut(Keys.Control | Keys.Shift | free[0]);
                Find<Button>(page, "saveSettingsButton").PerformClick();
                Check(Field<SettingsPage?>(main, "settingsPage") == null && Field<TableLayoutPanel>(main, "workspace").Visible,
                    "save commits valid edits and returns to the capture workspace");
            }

            Size defaultSize = main.Size;
            main.Size = main.MinimumSize;
            Find<Button>(main, "settingsButton").PerformClick();
            SettingsPage compactPage = Find<SettingsPage>(main, "SettingsPage");
            await Task.Delay(100);
            Panel scroller = Field<Panel>(compactPage, "scroller");
            ShortcutRecorder last = Find<ShortcutRecorder>(compactPage, "scrollShortcut");
            // The About section follows shortcuts; the very bottom no longer shows this recorder.
            scroller.ScrollControlIntoView(last);
            await Task.Delay(80);
            Check(scroller.RectangleToScreen(scroller.ClientRectangle).Contains(last.RectangleToScreen(last.ClientRectangle)),
                "last shortcut is reachable by scrolling at minimum window size");
            Check(Find<Button>(compactPage, "saveSettingsButton").RectangleToScreen(Find<Button>(compactPage, "saveSettingsButton").ClientRectangle).Bottom
                <= compactPage.RectangleToScreen(compactPage.ClientRectangle).Bottom,
                "save remains visible at minimum window size");
            using (Bitmap compact = new(main.Width, main.Height))
            {
                main.DrawToBitmap(compact, new Rectangle(Point.Empty, main.Size));
                compact.Save(Path.Combine(Output, "settings-minimum-" + main.DeviceDpi + ".png"), ImageFormat.Png);
            }
            Find<Button>(compactPage, "cancelSettingsButton").PerformClick();
            main.Size = defaultSize;

            candidate.Shortcuts["full"] = Keys.None;
            Check(Call(main, "TrySavePreferences", candidate) == null, "disabled hotkey saves successfully");
            Call(main, "RegisterCaptureHotkeys");
            Check(!Field<Dictionary<ushort, string>>(main, "hotkeyActions").ContainsValue("full")
                && Find<Button>(main, "fullButton").Enabled, "disabled shortcut keeps mouse capture entry available");
            using Bitmap mainImage = new(main.Width, main.Height);
            main.DrawToBitmap(mainImage, new Rectangle(Point.Empty, main.Size));
            mainImage.Save(Path.Combine(Output, "main-settings-entry-" + main.DeviceDpi + ".png"), ImageFormat.Png);
        }
        finally
        {
            Call(main, "CloseSettings");
            FieldInfo(main, "preferencesPath").SetValue(main, path);
            Call(main, "ReleaseCaptureHotkeys");
            FieldInfo(main, "preferences").SetValue(main, original);
            Call(main, "ApplyAppearance");
            if (previous == null) File.Delete(path); else File.WriteAllBytes(path, previous);
        }
    }
}
