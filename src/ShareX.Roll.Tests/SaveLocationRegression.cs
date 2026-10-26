using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UIA = System.Windows.Automation;

internal static class SaveLocationRegression
{
    private static readonly string Output = Path.GetFullPath("artifacts/save-location");
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
    private static object? Call(object target, string name, params object[] args) => target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static FieldInfo Member(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static T Field<T>(object target, string name) => (T)Member(target, name).GetValue(target)!;
    private static T Find<T>(Control target, string name) where T : Control => (T)target.Controls.Find(name, true).Single();

    internal static int ReadAfterRestart(string path)
    {
        try
        {
            ApplicationPreferences loaded = ApplicationPreferences.Load(path);
            using SaveFileDialog dialog = ScreenshotSaveDialog.Create(loaded, "restart.png");
            Console.WriteLine(loaded.UseDefaultSaveFolder + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(dialog.InitialDirectory)));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    internal static async Task RunAsync(MainForm main)
    {
        Directory.CreateDirectory(Output);
        string chosen = Directory.CreateDirectory(Path.Combine(Output, "指定文件夹 中文")).FullName;
        string temporary = Directory.CreateDirectory(Path.Combine(Output, "temporary")).FullName;
        string configuration = Path.Combine(Output, "preferences.json");
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        string lastFolder = HelpersOptions.LastSaveDirectory;
        bool oldMotion = UiMotion.UserEnabled;
        string preferencesPath = Field<string>(main, "preferencesPath");
        byte[]? oldFile = File.Exists(preferencesPath) ? File.ReadAllBytes(preferencesPath) : null;
        ApplicationPreferences preferences = new() { DefaultSaveFolder = chosen };
        foreach (string kind in preferences.Shortcuts.Keys.ToArray()) preferences.Shortcuts[kind] = Keys.None;
        try
        {
            Check(!preferences.UseDefaultSaveFolder, "new preferences default to the feature being off");
            File.WriteAllText(configuration, "{}");
            Check(!ApplicationPreferences.Load(configuration).UseDefaultSaveFolder, "older configurations migrate with the feature off");
            preferences.UseDefaultSaveFolder = true;
            preferences.Save(configuration);
            ApplicationPreferences loaded = ApplicationPreferences.Load(configuration);
            Check(loaded.UseDefaultSaveFolder && loaded.DefaultSaveFolder == chosen, "switch and Unicode folder survive serialization");
            ProcessStartInfo start = new(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add("--save-location-child"); start.ArgumentList.Add(configuration);
            using (Process child = Process.Start(start)!)
            {
                string output = await child.StandardOutput.ReadToEndAsync();
                string error = await child.StandardError.ReadToEndAsync();
                await child.WaitForExitAsync();
                Check(child.ExitCode == 0 && output.Trim() == "True|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(chosen)), "a fresh process restores the configured folder: " + error);
            }
            HelpersOptions.LastSaveDirectory = temporary;
            using (SaveFileDialog dialog = ScreenshotSaveDialog.Create(loaded, "capture"))
                Check(dialog.InitialDirectory == chosen && dialog.FilterIndex == 1 && dialog.DefaultExt == "png", "configured folder overrides history and new images default to PNG");
            using (SaveFileDialog dialog = ScreenshotSaveDialog.Create(loaded, Path.Combine(temporary, "edited.jpg")))
                Check(dialog.InitialDirectory == chosen && dialog.FileName == "edited.jpg" && dialog.FilterIndex == 2, "editor exports honor the folder and preserve the format");

            Member(main, "preferences").SetValue(main, loaded);
            using Bitmap image = new(44, 31);
            using (Graphics graphics = Graphics.FromImage(image)) graphics.Clear(Color.Coral);
            string marker = "capture-" + Guid.NewGuid().ToString("N");
            int savedCount = Directory.GetFiles(chosen).Length;
            Call(main, "SetPreview", (Bitmap)image.Clone());
            Check(Directory.GetFiles(chosen).Length == savedCount, "displaying a screenshot never saves it automatically");
            await WithDialog(main, () => Call(main, "SaveImage", image), marker);
            string saved = Path.Combine(chosen, marker + ".png");
            using (Bitmap actual = new(saved))
                Check(actual.Size == image.Size && actual.RawFormat.Guid == ImageFormat.Png.Guid && ImageHelpers.CompareImages(image, actual), "the real main-window save dialog opens the chosen folder and writes a valid PNG");

            string alternate = Path.Combine(temporary, marker + "-alternate.png");
            await WithDialog(main, () => Call(main, "SaveImage", image), alternate);
            Check(File.Exists(alternate) && HelpersOptions.LastSaveDirectory == temporary, "a save can temporarily use a different folder");
            await WithDialog(main, () => Call(main, "SaveImage", image), marker + "-again");
            Check(File.Exists(Path.Combine(chosen, marker + "-again.png")), "the next save returns to the configured folder after a temporary choice");
            await WithDialog(main, () => Call(main, "SaveImage", image), null);
            Check(HelpersOptions.LastSaveDirectory == chosen, "canceling the native save dialog leaves the last successful folder intact");

            using (RegionCaptureForm editor = ScreenshotEditor.Create(image, CapturePalette.Light, loaded))
            {
                editor.Show(main);
                string exportName = marker + "-editor";
                await WithDialog(editor, () => Call(editor, "OnSaveImageAsRequested"), exportName);
                Check(File.Exists(Path.Combine(chosen, exportName + ".png")), "the real editor export dialog uses the configured folder");
                editor.Close();
            }

            loaded.UseDefaultSaveFolder = false;
            HelpersOptions.LastSaveDirectory = temporary;
            using (SaveFileDialog dialog = ScreenshotSaveDialog.Create(loaded, "capture"))
                Check(dialog.InitialDirectory == temporary, "turning the feature off restores the last successful save folder");
            loaded.UseDefaultSaveFolder = true;
            loaded.DefaultSaveFolder = Path.Combine(Output, "missing-" + Guid.NewGuid().ToString("N"));
            using (SaveFileDialog dialog = ScreenshotSaveDialog.Create(loaded, "capture"))
                Check(dialog.InitialDirectory == temporary && dialog.Title == ShareX.Roll.Localization.UiText.L("SaveFolderUnavailableTitle"), "a missing folder falls back with an explanation in the native dialog");
            string fallbackName = marker + "-fallback";
            await WithDialog(main, () => Call(main, "SaveImage", image), fallbackName);
            Check(File.Exists(Path.Combine(temporary, fallbackName + ".png")), "saving still works when the configured folder is unavailable");
            Check(Call(main, "TrySavePreferences", loaded) is string, "settings reject an enabled missing folder without writing it");

            loaded.DefaultSaveFolder = chosen;
            loaded.UseDefaultSaveFolder = false;
            Call(main, "OpenSettings");
            await Task.Delay(260);
            SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
            Check(!Find<CheckBox>(page, "defaultSaveFolderToggle").Checked && !Find<Control>(page, "saveFolderPathRow").Visible, "disabled settings keep the folder controls collapsed");
            Find<CheckBox>(page, "defaultSaveFolderToggle").Checked = true;
            Check(Find<Control>(page, "saveFolderPathRow").Visible && !loaded.UseDefaultSaveFolder, "the switch exposes the folder controls while editing only a draft");
            await WithDialog(main, () => Find<Button>(page, "browseSaveFolderButton").PerformClick(), "@folder");
            Check(Find<TextBox>(page, "defaultSaveFolderPath").Text == chosen, "the native folder picker returns the selected folder");
            Find<Button>(page, "cancelSettingsButton").PerformClick();
            Check(!Field<ApplicationPreferences>(main, "preferences").UseDefaultSaveFolder, "cancel discards the save-folder draft");
            Call(main, "OpenSettings");
            await Task.Delay(260);
            page = Find<SettingsPage>(main, "SettingsPage");
            Find<CheckBox>(page, "defaultSaveFolderToggle").Checked = true;
            Find<Button>(page, "saveSettingsButton").PerformClick();
            Check(Field<SettingsPage?>(main, "settingsPage") == null && ApplicationPreferences.Load(preferencesPath).UseDefaultSaveFolder, "save and return commits the folder setting to disk");
            Call(main, "OpenSettings"); await Task.Delay(260);
            page = Find<SettingsPage>(main, "SettingsPage");
            Find<Button>(page, "resetSettingsButton").PerformClick();
            Check(!Find<CheckBox>(page, "defaultSaveFolderToggle").Checked && Find<TextBox>(page, "defaultSaveFolderPath").Text == "", "reset restores off and clears the folder in the draft");
            Find<Button>(page, "cancelSettingsButton").PerformClick();
        }
        finally
        {
            Call(main, "CloseSettings");
            Member(main, "preferences").SetValue(main, original);
            HelpersOptions.LastSaveDirectory = lastFolder;
            UiMotion.UserEnabled = oldMotion;
            if (oldFile == null) { if (File.Exists(preferencesPath)) File.Delete(preferencesPath); }
            else File.WriteAllBytes(preferencesPath, oldFile);
        }
    }

    internal static async Task RunVisualAsync(MainForm main, bool baseline)
    {
        string output = Path.Combine(Output, baseline ? "before" : "after");
        Directory.CreateDirectory(output);
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        Size originalSize = main.Size;
        bool originalMotion = UiMotion.UserEnabled;
        LanguageMode originalLanguage = original.Language;
        try
        {
            foreach (LanguageMode language in baseline ? new[] { LanguageMode.SimplifiedChinese } : new[] { LanguageMode.SimplifiedChinese, LanguageMode.TraditionalChinese, LanguageMode.English })
            foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            foreach (bool compact in new[] { false, true })
            {
                ApplicationPreferences preferences = original.Clone();
                preferences.Appearance = mode;
                preferences.Language = language;
                preferences.UseDefaultSaveFolder = true;
                preferences.DefaultSaveFolder = Path.Combine(Output, "项目截图与课程资料", "工程文件及长路径显示检查", "2026年10月", "图片原稿与分享导出");
                Directory.CreateDirectory(preferences.DefaultSaveFolder);
                foreach (string kind in preferences.Shortcuts.Keys.ToArray()) preferences.Shortcuts[kind] = Keys.None;
                Member(main, "preferences").SetValue(main, preferences);
                UiLanguage.Apply(language);
                main.Size = compact ? main.MinimumSize : originalSize;
                Call(main, "ApplyAppearance");
                Call(main, "OpenSettings");
                await Task.Delay(280);
                SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
                Panel viewport = Field<Panel>(page, "scroller");
                Control saveLocation = Find<Control>(page, "saveLocationOptions");
                viewport.ScrollControlIntoView(saveLocation);
                Find<Button>(page, "browseSaveFolderButton").Focus();
                await Task.Delay(100);
                using Bitmap screenshot = new(main.Width, main.Height);
                main.DrawToBitmap(screenshot, new Rectangle(Point.Empty, main.Size));
                TransitionSnapshot.RestoreNativeTextColors(main, screenshot);
                screenshot.Save(Path.Combine(output, $"settings-{language}-{mode}-{(compact ? "compact" : "regular")}-{main.DeviceDpi}.png"), ImageFormat.Png);
                if (!baseline)
                {
                    Check(!viewport.HorizontalScroll.Visible, $"{mode}/{compact}: settings fit without a horizontal scrollbar");
                    Control path = Find<Control>(page, "defaultSaveFolderPath");
                    Control browse = Find<Control>(page, "browseSaveFolderButton");
                    Check(browse.Height == 64 * main.DeviceDpi / 96 &&
                        ButtonElevation.FaceBounds(browse.ClientSize, main.DeviceDpi / 96f).Height == 40 * main.DeviceDpi / 96,
                        $"{mode}/{compact}: folder controls scale once with the display DPI");
                    Check(path.Width > 100 * main.DeviceDpi / 96f && browse.Right <= browse.Parent!.ClientSize.Width, $"{mode}/{compact}: long paths retain room for the browse action");
                    Check(path.AccessibleName != null && Find<CheckBox>(page, "defaultSaveFolderToggle").AccessibleDescription != null,
                        $"{mode}/{compact}: the path and switch expose names and purpose to accessibility");
                    TextBox text = (TextBox)path;
                    Check(text.ReadOnly && text.SelectionStart == text.TextLength, $"{mode}/{compact}: the full path stays copyable and the useful tail is visible");
                    if (language == LanguageMode.SimplifiedChinese && !compact)
                        await VerifyFeedback(page, output, mode, main.DeviceDpi);
                }
                Call(main, "CloseSettings"); await Task.Delay(230);
            }
        }
        finally
        {
            Call(main, "CloseSettings");
            Member(main, "preferences").SetValue(main, original);
            main.Size = originalSize;
            UiLanguage.Apply(originalLanguage);
            Call(main, "ApplyAppearance");
            UiMotion.UserEnabled = originalMotion;
        }
    }

    private static async Task VerifyFeedback(SettingsPage page, string output, AppearanceMode mode, int dpi)
    {
        Control group = Find<Control>(page, "saveLocationGroup");
        CheckBox toggle = Find<CheckBox>(page, "defaultSaveFolderToggle");
        UiTween position = Field<UiTween>(toggle, "position");
        toggle.Checked = false;
        int layouts = 0;
        LayoutEventHandler count = (_, _) => layouts++;
        group.Layout += count;
        await Task.Delay(50);
        if (UiMotion.Enabled) Check(position.IsRunning && position.Value > 0 && position.Value < 1, mode + ": switch moves through an intermediate state");
        await Task.Delay(350);
        group.Layout -= count;
        Check(layouts == 0 && !position.IsRunning, mode + ": switch animation performs no repeated card layout and stops when settled");
        toggle.Checked = true;
        bool motion = UiMotion.UserEnabled;
        UiMotion.UserEnabled = false;
        toggle.Checked = false;
        Check(position.Value == 0 && !position.IsRunning && !Find<Control>(page, "saveFolderPathRow").Visible, mode + ": reduced motion immediately presents the correct disabled state");
        toggle.Checked = true;
        Check(position.Value == 1 && !position.IsRunning, mode + ": reduced motion immediately presents the correct enabled state");
        UiMotion.UserEnabled = motion;
        Call(toggle, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 8, 8, 0));
        Check(Field<UiTween>(toggle, "press").Value == 1, mode + ": pointer-down feedback is immediate");
        Call(toggle, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 8, 8, 0));
        await Task.Delay(180);
        SendMessage(page.FindForm()!.Handle, 0x128, new IntPtr(0x10002), IntPtr.Zero);
        toggle.Focus();
        Check(toggle.AccessibilityObject.Role == AccessibleRole.CheckButton &&
            toggle.AccessibilityObject.State.HasFlag(AccessibleStates.Checked), mode + ": the switch retains native checkbox accessibility and state");
        using (Bitmap focus = new(toggle.Width, toggle.Height))
        {
            toggle.DrawToBitmap(focus, toggle.ClientRectangle);
            focus.Save(Path.Combine(output, $"switch-focus-{mode}-{dpi}.png"), ImageFormat.Png);
        }
        TextBox text = Find<TextBox>(page, "defaultSaveFolderPath");
        text.Focus(); await Task.Delay(140);
        Control field = Find<Control>(page, "defaultSaveFolderField");
        using (Bitmap focusedField = new(field.Width, field.Height))
        {
            field.DrawToBitmap(focusedField, field.ClientRectangle);
            focusedField.Save(Path.Combine(output, $"folder-focus-{mode}-{dpi}.png"), ImageFormat.Png);
        }
        text.Parent!.Parent!.Focus();
        string previous = text.Text;
        SaveLocationOptions options = Find<SaveLocationOptions>(page, "saveLocationOptions");
        options.LoadPreferences(new ApplicationPreferences { UseDefaultSaveFolder = true });
        Check(!Find<Label>(page, "saveFolderValidation").Visible && !text.Visible, mode + ": an empty selection uses the compact field placeholder without a duplicate prompt");
        options.LoadPreferences(new ApplicationPreferences { UseDefaultSaveFolder = true, DefaultSaveFolder = previous });
        Check(!Find<Label>(page, "saveFolderValidation").Visible, mode + ": a valid selection removes the inline hint");
        Dictionary<Control, Font> fonts = new();
        // Double explicit text styles, without compounding the size inherited by each child.
        void Collect(Control control) { fonts[control] = control.Font; foreach (Control child in control.Controls) Collect(child); }
        fonts.Clear(); Collect(group);
        foreach (var pair in fonts) pair.Key.Font = new Font(pair.Value.FontFamily, pair.Value.Size * 2, pair.Value.Style);
        try
        {
            group.PerformLayout();
            Control browse = Find<Control>(page, "browseSaveFolderButton");
            Check(text.Height >= text.Font.Height && text.Bottom <= text.Parent!.ClientSize.Height && browse.Right <= browse.Parent!.ClientSize.Width,
                mode + ": the new folder controls accommodate text at 200 percent");
            using Bitmap large = new(group.Width, group.Height);
            group.DrawToBitmap(large, group.ClientRectangle);
            large.Save(Path.Combine(output, $"folder-text200-{mode}-{dpi}.png"), ImageFormat.Png);
        }
        finally
        {
            foreach (var pair in fonts) { Font scaled = pair.Key.Font; pair.Key.Font = pair.Value; scaled.Dispose(); }
        }
    }

    // Exercise the real Windows picker. A basename verifies the actual initial folder, not just a property.
    private static async Task WithDialog(Form owner, Action open, string? filename)
    {
        uint thread = GetWindowThreadProcessId(owner.Handle, out _);
        Task automate = Task.Run(async () =>
        {
            IntPtr dialog = IntPtr.Zero;
            try
            {
                Stopwatch deadline = Stopwatch.StartNew();
                while (dialog == IntPtr.Zero && deadline.Elapsed.TotalSeconds < 12)
                {
                    EnumThreadWindows(thread, (window, _) =>
                    {
                        StringBuilder name = new(256); GetClassName(window, name, name.Capacity);
                        if (name.ToString() == "#32770" && IsWindowVisible(window)) { dialog = window; return false; }
                        return true;
                    }, IntPtr.Zero);
                    if (dialog == IntPtr.Zero) await Task.Delay(60);
                }
                if (dialog == IntPtr.Zero) throw new TimeoutException("Native dialog did not open");
                if (filename == null) { PostMessage(dialog, 0x10, IntPtr.Zero, IntPtr.Zero); return; }
                UIA.AutomationElement root = UIA.AutomationElement.FromHandle(dialog);
                UIA.AutomationElement? saveButton = null;
                while (deadline.Elapsed.TotalSeconds < 12)
                {
                    saveButton = root.FindFirst(UIA.TreeScope.Descendants, new UIA.AndCondition(
                        new UIA.PropertyCondition(UIA.AutomationElement.AutomationIdProperty, "1"),
                        new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.Button)));
                    if (saveButton != null && saveButton.Current.IsEnabled) break;
                    await Task.Delay(60);
                }
                if (filename != "@folder")
                {
                    UIA.AutomationElement edit = root.FindFirst(UIA.TreeScope.Descendants, new UIA.AndCondition(
                        new UIA.PropertyCondition(UIA.AutomationElement.AutomationIdProperty, "1001"),
                        new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.Edit)))
                        ?? throw new InvalidOperationException("Native filename field missing");
                    ((UIA.ValuePattern)edit.GetCurrentPattern(UIA.ValuePattern.Pattern)).SetValue(filename);
                }
                if (saveButton == null) throw new InvalidOperationException("Native confirmation button missing");
                PostMessage(dialog, 0x111, new IntPtr(1), new IntPtr(saveButton.Current.NativeWindowHandle));
                if (filename == "@folder")
                {
                    // SelectedPath can initially select the leaf in its parent. Enter it, then confirm.
                    await Task.Delay(500);
                    if (IsWindowVisible(dialog)) PostMessage(dialog, 0x111, new IntPtr(1), new IntPtr(saveButton.Current.NativeWindowHandle));
                }
                while (IsWindowVisible(dialog) && deadline.Elapsed.TotalSeconds < 12) await Task.Delay(60);
                if (IsWindowVisible(dialog)) throw new TimeoutException("Native save did not complete");
            }
            catch { if (dialog != IntPtr.Zero) PostMessage(dialog, 0x10, IntPtr.Zero, IntPtr.Zero); throw; }
        });
        open();
        await automate;
    }

    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr first, IntPtr second);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr first, IntPtr second);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
}
