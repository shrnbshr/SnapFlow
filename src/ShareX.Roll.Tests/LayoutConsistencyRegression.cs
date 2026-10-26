using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

internal static class LayoutConsistencyRegression
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private static void Save(Control control, string path)
    {
        using Bitmap image = new(control.Width, control.Height);
        control.DrawToBitmap(image, control.ClientRectangle);
        image.Save(path, ImageFormat.Png);
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/ui-consistency/" + main.DeviceDpi);
        Directory.CreateDirectory(output);
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        Size originalSize = main.Size;
        try
        {
            Call(main, "ReleaseCaptureHotkeys");
            foreach (LanguageMode language in new[] { LanguageMode.SimplifiedChinese, LanguageMode.TraditionalChinese, LanguageMode.English })
            foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            foreach (bool compact in new[] { false, true })
            {
                ApplicationPreferences working = original.Clone();
                working.Language = language;
                working.Appearance = mode;
                working.AnimationsEnabled = false;
                working.UseDefaultSaveFolder = false;
                working.DefaultSaveFolder = "";
                foreach (string key in working.Shortcuts.Keys.ToArray()) working.Shortcuts[key] = Keys.None;
                main.GetType().GetField("preferences", Private)!.SetValue(main, working);
                UiLanguage.Apply(language);
                Call(main, "ApplyWorkspaceLanguage");
                Call(main, "ApplyAppearance");
                main.Size = compact ? main.MinimumSize : originalSize;
                await Task.Delay(40);
                string context = $"{language}-{mode}-{(compact ? "minimum" : "regular")}";
                float dpi = main.DeviceDpi / 96f;
                Control[] captures = ApplicationPreferences.Actions.Select(action => Find<Control>(main, action.Kind + "Button")).ToArray();
                Check(captures.Select(button => button.Height).Distinct().Count() == 1, context + ": capture choices have equal heights");
                Check(captures[1].Top - captures[0].Bottom == captures[2].Top - captures[1].Bottom,
                    context + ": capture choices have equal gaps");
                Save(main, Path.Combine(output, context + "-main.png"));
                Call(main, "OpenSettings");
                await Task.Delay(80);
                SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
                Control[] themes = new[] { "themeSystem", "themeLight", "themeDark" }.Select(name => Find<Control>(page, name)).ToArray();
                Check(themes.Max(theme => theme.Width) - themes.Min(theme => theme.Width) <= 2,
                    context + ": the three appearance cards are equally wide");
                TableLayoutPanel[] rows = new[] { "transparencyRow", "motionRow", "languageRow", "saveFolderHeader" }
                    .Select(name => Find<TableLayoutPanel>(page, name)).ToArray();
                Console.WriteLine("ROWS: " + context + " " + string.Join("; ", rows.Select(row => $"{row.Name}={row.Bounds} padding={row.Padding}")));
                Check(rows.Max(row => row.Height) - rows.Min(row => row.Height) <= 1, context + ": option headers share one height even when captions wrap");
                Check(Find<Control>(page, "languageSelector").Height == 40 * dpi,
                    context + ": language selection uses the standard input height");
                Check(Find<Control>(page, "scrollingOptionsRow").Height == 64 * dpi,
                    context + ": collapsed scrolling options use the standard row height");
                foreach (TableLayoutPanel row in rows)
                {
                    FlowLayoutPanel caption = row.Controls.OfType<FlowLayoutPanel>().Single();
                    Control accessory = row.Controls.Cast<Control>().Single(control => control != caption);
                    Check(caption.Left == row.Padding.Left && accessory.Right == row.ClientSize.Width - row.Padding.Right,
                        context + "/" + row.Name + ": content uses the common left and right insets");
                    Check(Math.Abs(caption.Top - (row.Height - caption.Bottom)) <= 1,
                        context + "/" + row.Name + ": caption has balanced vertical whitespace");
                    Check(caption.Controls.OfType<Label>().All(label => label.Bottom <= caption.Height && label.Right <= caption.Width),
                        context + "/" + row.Name + ": every caption is fully laid out");
                }
                Control content = Field<Control>(page, "content");
                Control saving = Find<Control>(page, "saveLocationOptions");
                Control scrolling = Find<Control>(page, "scrollingOptionsRow");
                int gap = (int)(SettingsLayout.Gap * dpi);
                Check(rows[1].Top - rows[0].Bottom == gap && rows[2].Top - rows[1].Bottom == gap && scrolling.Top - saving.Bottom == gap,
                    context + ": regular options and the nested save-folder control share one vertical gap");
                Control shortcutList = Find<Control>(page, "shortcutList");
                Control[] shortcutRows = ApplicationPreferences.Actions.Select(action => Find<Control>(page, action.Kind + "ShortcutRow")).ToArray();
                Check(shortcutRows.All(row => row.Parent == shortcutList && row.Height == SettingsLayout.RowHeight * dpi) &&
                    shortcutRows[1].Top - shortcutRows[0].Bottom == gap && shortcutRows[2].Top - shortcutRows[1].Bottom == gap,
                    context + ": each shortcut has its own standard-height card and the same option gap");
                Save(content, Path.Combine(output, context + "-settings.png"));
                Find<CheckBox>(page, "defaultSaveFolderToggle").Checked = true;
                await Task.Delay(60);
                Control folder = Find<Control>(page, "defaultSaveFolderField");
                Button browse = Find<Button>(page, "browseSaveFolderButton");
                RectangleF face = ButtonElevation.FaceBounds(browse.ClientSize, dpi);
                Console.WriteLine($"FOLDER: {context} field={folder.Bounds}; browse={browse.Bounds}; visible={face}");
                Check(browse.Height <= 66 * dpi && folder.Height <= 42 * dpi,
                    context + ": revealing a hidden folder picker does not double-scale or stretch the controls");
                Check(Math.Abs(folder.Height - face.Height) <= dpi && Math.Abs(folder.Top + folder.Height / 2f - browse.Top - browse.Height / 2f) <= 1,
                    context + ": path field and browse face share one height and centerline");
                Save(Find<Control>(page, "saveLocationGroup"), Path.Combine(output, context + "-folder.png"));
                AboutLinkButton repository = Find<AboutLinkButton>(page, "aboutRepository");
                AboutLinkButton account = Find<AboutLinkButton>(page, "aboutGitHubAccount");
                Check(repository.Top == account.Top && Math.Abs(repository.Width - account.Width) <= 1 && repository.Height == account.Height,
                    context + ": project cards remain equal and on the same row");
                Control project = Find<Control>(page, "aboutProject");
                Check(project.Left >= 20 * dpi && project.Parent!.Width - project.Right >= 20 * dpi,
                    context + ": the project links have the wider, balanced outer horizontal insets");
                Check(repository.DetailAvailable >= repository.DetailWidth && account.DetailAvailable >= account.DetailWidth,
                    context + ": the wider project insets still leave both link details readable");
                Check(!Field<Panel>(page, "scroller").HorizontalScroll.Visible,
                    context + ": settings do not need horizontal scrolling");
                foreach (string name in new[] { "saveLocationGroup", "scrollingOptionsRow" })
                {
                    Control group = Find<Control>(page, name);
                    using Bitmap corner = new(group.Width, group.Height);
                    group.DrawToBitmap(corner, group.ClientRectangle);
                    Check(corner.GetPixel(0, 0).ToArgb() == CapturePalette.For(mode).Background.ToArgb(),
                        context + "/" + name + ": child headers preserve the outer rounded corners");
                }
                Find<Button>(page, "scrollingOptionsButton").PerformClick();
                await Task.Delay(60);
                Check(scrolling.Top - saving.Bottom == gap, context + ": revealing dependent controls preserves the option gap");
                Save(content, Path.Combine(output, context + "-expanded.png"));
                Call(main, "CloseSettings");
                if (!compact)
                {
                    // Suppress only automatic region selection while inspecting the real scrolling window.
                    using ScrollingCaptureForm scroll = (ScrollingCaptureForm)Activator.CreateInstance(typeof(ScrollingCaptureForm), Private,
                        null, [new ScrollingCaptureOptions()], null)!;
                    scroll.Load -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), scroll,
                        typeof(ScrollingCaptureForm).GetMethod("ScrollingCaptureForm_Load", Private)!);
                    scroll.WindowState = FormWindowState.Normal;
                    _ = scroll.Handle;
                    scroll.ClientSize = new Size((int)(900 * dpi), (int)(600 * dpi));
                    scroll.Text = ShareX.Roll.Localization.UiText.L("ScrollTitle");
                    Find<Button>(scroll, "btnUpload").Text = ShareX.Roll.Localization.UiText.L("SaveImage");
                    Call(main, "ApplyScrollingAppearance", scroll);
                    scroll.Show(main);
                    await Task.Delay(40);
                    Save(scroll, Path.Combine(output, context + "-scroll-window.png"));
                    Button[] actions = new[] { "btnCapture", "btnUpload", "btnCopy", "btnOptions", "btnHelp" }
                        .Select(name => Find<Button>(scroll, name)).ToArray();
                    Check(actions.Max(button => button.Height) - actions.Min(button => button.Height) <= 1 && actions[0].Height <= 66 * dpi,
                        context + ": scrolling-window actions share the same compact height");
                    Check(scroll.OptionsRequested != null, context + ": scrolling options use the app's shared settings presentation");
                    int delay = scroll.Options.StartDelay;
                    using (ScrollingOptionsDialog options = new(scroll.Options, CapturePalette.For(mode), working.TransparencyEnabled))
                    {
                        options.Show(scroll);
                        await Task.Delay(50);
                        Check(options.ClientSize.Width >= 560 * dpi && Find<Control>(options, "cbAutoScrollTop").Width >= 40 * dpi &&
                            Find<Control>(options, "nudStartDelayField").Height == 40 * dpi && Find<Control>(options, "cbScrollMethod").Height == 40 * dpi,
                            context + ": scrolling options scale their window, fields and switches together");
                        Save(options, Path.Combine(output, context + "-scroll-options.png"));
                        Find<NumericUpDown>(options, "nudStartDelay").Value = delay + 100;
                        Find<Button>(options, "cancelScrollOptions").PerformClick();
                        Check(scroll.Options.StartDelay == delay, context + ": canceling scrolling options preserves the original settings");
                    }
                    using (ScrollingOptionsDialog options = new(scroll.Options, CapturePalette.For(mode), working.TransparencyEnabled))
                    {
                        options.Show(scroll);
                        Find<NumericUpDown>(options, "nudStartDelay").Value = delay + 100;
                        Find<Button>(options, "saveScrollOptions").PerformClick();
                        Check(scroll.Options.StartDelay == delay + 100 && options.DialogResult == DialogResult.OK,
                            context + ": saving scrolling options updates the original capture manager options");
                    }
                }
            }
        }
        finally
        {
            Call(main, "CloseSettings");
            main.GetType().GetField("preferences", Private)!.SetValue(main, original);
            main.Size = originalSize;
            UiLanguage.Apply(original.Language);
            Call(main, "ApplyWorkspaceLanguage");
            Call(main, "ApplyAppearance");
            Call(main, "RegisterCaptureHotkeys");
        }
    }
}
