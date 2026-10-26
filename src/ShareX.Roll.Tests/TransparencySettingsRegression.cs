using ShareX.HelpersLib;
using ShareX.Roll;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class TransparencySettingsRegression
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    private static void VerifyMaterial(MainForm main, bool requested, string context)
    {
        bool expected = requested && WindowBackdrop.IsSupported();
        Check(Field<WindowBackdrop>(main, "windowBackdrop").IsActive == expected, context + ": actual material respects the app choice and system restrictions");
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            Check(DwmGetWindowAttribute(main.Handle, 38, out int material, sizeof(int)) >= 0 && material == (expected ? 3 : 1),
                context + ": the native DWM material agrees with the setting");
    }

    internal static void VerifyStartup(MainForm main, bool expected)
    {
        Check(Field<ApplicationPreferences>(main, "preferences").TransparencyEnabled == expected, "fresh process reads the saved transparency choice");
        VerifyMaterial(main, expected, "cold startup before any test reapply");
    }

    private static async Task VerifyRestart(bool expected, string output)
    {
        ProcessStartInfo start = new(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("--transparency-startup");
        if (!expected) start.ArgumentList.Add("--expected-off");
        using Process child = Process.Start(start)!;
        Task<string> stdout = child.StandardOutput.ReadToEndAsync(), stderr = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        string log = await stdout + await stderr;
        File.WriteAllText(Path.Combine(output, $"restart-{expected}.log"), log);
        Check(child.ExitCode == 0 && log.Contains("PASS: cold startup"), "restarting the application preserves transparency=" + expected);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
            foreach (Control descendant in Descendants(child)) yield return descendant;
    }

    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double C(byte value) { double s = value / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
            return .2126 * C(c.R) + .7152 * C(c.G) + .0722 * C(c.B);
        }
        double first = L(a), second = L(b);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }

    private static void VerifyRow(SettingsPage page, string context)
    {
        Control row = Find<Control>(page, "transparencyRow"), caption = Find<Control>(page, "transparencyCaption");
        CheckBox toggle = Find<CheckBox>(page, "transparencyToggle");
        Label hint = Find<Label>(page, "transparencyHint");
        Check(toggle.TabStop && toggle.Enabled && toggle.Width >= 40 * page.DeviceDpi / 96 && toggle.Height >= 28 * page.DeviceDpi / 96,
            context + ": switch keeps a keyboard focus target and a usable DPI-scaled hit area");
        Check(toggle.AccessibleName == LocalizationText("Transparency") && toggle.AccessibleDescription == hint.Text
            && toggle.AccessibilityObject.Role == AccessibleRole.CheckButton,
            context + ": native checkbox semantics have a localized label and description");
        Check(hint.Text == LocalizationText(WindowBackdrop.IsSupported() ? "TransparencyHint" : "TransparencyUnavailable"),
            context + ": explanation matches system availability");
        Check(caption.Right <= toggle.Left && toggle.Right <= row.ClientSize.Width - row.Padding.Right && toggle.Top >= row.Padding.Top,
            context + ": text and switch do not overlap at this width");
        foreach (Label label in caption.Controls.OfType<Label>())
        {
            Check(label.Bottom <= caption.ClientSize.Height && label.Right <= caption.ClientSize.Width,
                context + ": the complete label fits its caption");
            Check(Contrast(label.ForeColor, label.BackColor) >= 4.5, context + $": label contrast is {Contrast(label.ForeColor, label.BackColor):F2}:1");
        }
        Panel viewport = Field<Panel>(page, "scroller");
        Check(!viewport.HorizontalScroll.Visible, context + ": the page needs no horizontal scrolling");
    }

    private static string LocalizationText(string key) => ShareX.Roll.Localization.UiText.L(key);
    private static void SaveRow(SettingsPage page, string output, string name)
    {
        Control row = Find<Control>(page, "transparencyRow");
        using Bitmap image = new(row.Width, row.Height);
        row.DrawToBitmap(image, row.ClientRectangle);
        image.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/transparency-settings/" + main.DeviceDpi);
        Directory.CreateDirectory(output);
        string path = Field<string>(main, "preferencesPath");
        byte[]? previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        Size originalSize = main.Size;
        bool originalTopMost = main.TopMost;
        try
        {
            string legacy = Path.Combine(output, "legacy.json");
            File.WriteAllText(legacy, "{\"Appearance\":2,\"AnimationsEnabled\":false,\"DefaultSaveFolder\":\"old-folder\"}");
            ApplicationPreferences migrated = ApplicationPreferences.Load(legacy);
            Check(migrated.TransparencyEnabled && migrated.Appearance == AppearanceMode.Dark && !migrated.AnimationsEnabled && migrated.DefaultSaveFolder == "old-folder",
                "legacy preferences default to transparency on while retaining existing choices");
            migrated.TransparencyEnabled = false;
            Check(!migrated.Clone().TransparencyEnabled, "cloning preserves a disabled transparency choice");
            Call(main, "CloseSettings");
            Call(main, "EndPageTransition");
            Call(main, "ReleaseCaptureHotkeys");
            ApplicationPreferences working = original.Clone();
            working.TransparencyEnabled = true;
            working.AnimationsEnabled = true;
            foreach (string key in working.Shortcuts.Keys.ToArray()) working.Shortcuts[key] = Keys.None;
            Set(main, "preferences", working);
            working.Save(path);
            byte[] savedBeforePreviews = File.ReadAllBytes(path);
            main.TopMost = true;
            main.ForceActivate();
            Call(main, "ApplyAppearance");

            foreach (LanguageMode language in new[] { LanguageMode.SimplifiedChinese, LanguageMode.TraditionalChinese, LanguageMode.English })
            foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            foreach (bool compact in new[] { false, true })
            {
                working.Language = language;
                working.Appearance = mode;
                UiLanguage.Apply(language);
                Call(main, "ApplyAppearance");
                main.Size = compact ? main.MinimumSize : originalSize;
                Call(main, "OpenSettings");
                await Task.Delay(300);
                SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
                Panel viewport = Field<Panel>(page, "scroller");
                Control row = Find<Control>(page, "transparencyRow");
                viewport.ScrollControlIntoView(row);
                await Task.Delay(100);
                string context = $"{language}-{mode}-{(compact ? "compact" : "regular")}";
                VerifyRow(page, context);
                CheckBox toggle = Find<CheckBox>(page, "transparencyToggle");
                toggle.Checked = false;
                VerifyMaterial(main, false, context + " preview off");
                Find<RadioButton>(page, "theme" + (mode == AppearanceMode.Dark ? AppearanceMode.Light : AppearanceMode.Dark)).Checked = true;
                VerifyMaterial(main, false, context + " theme change while preview is off");
                SendMessage(main.Handle, 0x31E, 0, 0);
                VerifyMaterial(main, false, context + " DWM composition recovery");
                Find<RadioButton>(page, "theme" + mode).Checked = true;
                await Task.Delay(240);
                SaveRow(page, output, context + "-off");
                toggle.AccessibilityObject.DoDefaultAction();
                Check(toggle.Checked && toggle.AccessibilityObject.State.HasFlag(AccessibleStates.Checked), context + ": accessible toggle action changes the checked state");
                VerifyMaterial(main, true, context + " preview on");
                await Task.Delay(240);
                SaveRow(page, output, context + "-on");
                if (language == LanguageMode.SimplifiedChinese && !compact && mode == AppearanceMode.Light)
                    await VerifyMotion(main, page, output);
                foreach (Label label in Find<Control>(page, "transparencyCaption").Controls.OfType<Label>())
                    label.Font = new Font(label.Font.FontFamily, label.Font.Size * 2, label.Font.Style);
                page.PerformLayout();
                VerifyRow(page, context + " 200% label font");
                toggle.Checked = false;
                Call(main, "CloseSettings");
                PageTransition? cover = Field<PageTransition?>(main, "pageTransition");
                if (cover != null && Field<WindowBackdrop>(main, "windowBackdrop").IsActive)
                {
                    System.Collections.IDictionary windows = Field<System.Collections.IDictionary>(Field<WindowBackdrop>(main, "windowBackdrop"), "windows");
                    int[] pixels = Field<int[]>(windows[cover]!, "pixels");
                    int inset = 4 * main.DeviceDpi / 96;
                    Check(pixels.Length > inset * cover.Width + inset && ((uint)pixels[inset * cover.Width + inset] >> 24) < 255,
                        context + ": cancel paints a translucent return frame instead of an opaque cover");
                }
                Call(main, "EndPageTransition");
                VerifyMaterial(main, true, context + " cancel");
            }
            Check(File.ReadAllBytes(path).SequenceEqual(savedBeforePreviews), "previews and cancellation never write the saved preferences file");
            foreach (bool enabled in new[] { false, true })
            {
                Call(main, "OpenSettings");
                await Task.Delay(250);
                SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
                Find<CheckBox>(page, "transparencyToggle").Checked = enabled;
                Find<Button>(page, "saveSettingsButton").PerformClick();
                await Task.Delay(250);
                Check(Field<SettingsPage?>(main, "settingsPage") == null && ApplicationPreferences.Load(path).TransparencyEnabled == enabled
                    && Field<ApplicationPreferences>(main, "preferences").TransparencyEnabled == enabled,
                    "save-and-return persists transparency=" + enabled);
                VerifyMaterial(main, enabled, "saved workspace");
                typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
                VerifyMaterial(main, enabled, "native handle recreated");
                await VerifyRestart(enabled, output);
            }
            Call(main, "OpenSettings");
            await Task.Delay(250);
            SettingsPage resetPage = Find<SettingsPage>(main, "SettingsPage");
            Find<CheckBox>(resetPage, "transparencyToggle").Checked = false;
            Find<Button>(resetPage, "resetSettingsButton").PerformClick();
            Check(Find<CheckBox>(resetPage, "transparencyToggle").Checked, "reset restores the transparency-on default");
            VerifyMaterial(main, true, "reset preview");
            Console.WriteLine("PASS: transparency settings flow and presentation completed");
        }
        finally
        {
            Call(main, "CloseSettings");
            Call(main, "EndPageTransition");
            Set(main, "preferences", original);
            if (previous == null) { if (File.Exists(path)) File.Delete(path); }
            else File.WriteAllBytes(path, previous);
            main.Size = originalSize;
            main.TopMost = originalTopMost;
            UiLanguage.Apply(original.Language);
            Call(main, "ApplyWorkspaceLanguage");
            Call(main, "ApplyAppearance");
            Call(main, "RegisterCaptureHotkeys");
        }
    }

    private static async Task VerifyMotion(MainForm main, SettingsPage page, string output)
    {
        CheckBox toggle = Find<CheckBox>(page, "transparencyToggle");
        UiTween position = Field<UiTween>(toggle, "position");
        Control row = Find<Control>(page, "transparencyRow");
        toggle.Focus();
        Check(toggle.Focused, "the transparency switch accepts keyboard focus");
        SendMessage(toggle.Handle, 0x100, (nint)Keys.Space, 0);
        SendMessage(toggle.Handle, 0x101, (nint)Keys.Space, 0);
        Check(!toggle.Checked, "the native Space key path switches transparency off");
        await Task.Delay(35);
        float before = position.Value;
        int layouts = 0;
        LayoutEventHandler count = (_, _) => layouts++;
        toggle.Checked = true;
        Check(Math.Abs(position.Value - before) < .001, "rapid reversal starts at the current visible thumb position");
        row.Layout += count;
        await Task.Delay(40);
        if (UiMotion.Enabled) Check(position.Value > 0 && position.Value < 1, "the switch paints a real intermediate thumb position");
        SaveRow(page, output, "switch-intermediate");
        await Task.Delay(500);
        row.Layout -= count;
        Check(layouts == 0 && !position.IsRunning, "switch animation performs no row layout per frame and stops its timer");
        Find<CheckBox>(page, "animationsToggle").Checked = false;
        toggle.Checked = false;
        Check(position.Value == 0 && !position.IsRunning, "reduced motion immediately shows the disabled state");
        toggle.Checked = true;
        Check(position.Value == 1 && !position.IsRunning, "reduced motion immediately shows the enabled state");
        Find<CheckBox>(page, "animationsToggle").Checked = true;
        VerifyMaterial(main, true, "after motion checks");
    }
}
