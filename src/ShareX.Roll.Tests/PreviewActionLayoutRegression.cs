using ShareX.Roll;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

internal static class PreviewActionLayoutRegression
{
    private static int checks;
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Call(object target, string name, params object[] values) => target.GetType().GetMethod(name, Private)!.Invoke(target, values);
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        checks++;
        Console.WriteLine("PASS: " + message);
    }

    private static void Verify(MainForm main, string context)
    {
        Control toolbar = Find<Control>(main, "previewToolbar");
        Control frame = Find<Control>(main, "previewSurface");
        Rectangle ScreenBounds(Control control) => control.RectangleToScreen(control.ClientRectangle);
        string[] names = [.. ApplicationPreferences.Actions.Select(action => action.Kind + "Button"),
            "editButton", "copyButton", "saveButton", "settingsButton"];
        Button[] buttons = names.Select(name => Find<Button>(main, name)).Where(button => button.Visible).ToArray();
        Check(buttons.Length == (Find<PreviewCanvas>(main, "previewCanvas").Image != null ? 7 : 4),
            context + ": image actions are shown only with a preview");
        Check(buttons.All(button => ScreenBounds(toolbar).Contains(ScreenBounds(button)) && button.Top >= 0 &&
            button.AccessibleName == button.Text && WorkspaceToolTip.GetCaption((CaptureModeButton)button).StartsWith(button.Text, StringComparison.Ordinal)),
            context + ": every visible icon fits in the bar and has a localized name and tooltip");
        Check(buttons.Select(button => ScreenBounds(button).Top).Distinct().Count() == 1 &&
            buttons.All(button => buttons.All(other => button == other || !ScreenBounds(button).IntersectsWith(ScreenBounds(other)))) &&
            ScreenBounds(toolbar).Bottom == ScreenBounds(frame).Top && ScreenBounds(frame).Width == main.ClientSize.Width,
            context + ": one row of commands sits above the full-width preview");
        Check(buttons.All(button => button.Width == Math.Round(44 * main.DeviceDpi / 96f) &&
            button.Region is { } region && region.IsVisible(button.Width / 2, button.Height / 2) &&
            !region.IsVisible(0, button.Height / 2)), context + ": compact command faces scale and match their hit regions");
    }

    private static void VerifyHints(MainForm main, string output)
    {
        WorkspaceToolTip tip = Field<WorkspaceToolTip>(main, "workspaceTips");
        CaptureModeButton region = Find<CaptureModeButton>(main, "regionButton");
        CaptureModeButton settings = Find<CaptureModeButton>(main, "settingsButton");
        Control? selected = main.ActiveControl;
        Check(!tip.Visible, "the hint stays hidden until a toolbar button is entered");
        string shortcut = region.Shortcut;
        try
        {
            region.Shortcut = "Ctrl+Shift+A";
            Call(region, "OnMouseEnter", EventArgs.Empty);
            Check(tip.Visible && tip.Text == $"{region.Text} (Ctrl+Shift+A)" && main.ActiveControl == selected,
                "pointer entry opens the localized hint immediately without taking focus");
            Check(tip.Region is { } shape && !shape.IsVisible(0, 0) && shape.IsVisible(tip.Width / 2, tip.Height / 2),
                "the hint uses a rounded outline");
            Save(main, output, "capture-tooltip");
            Call(settings, "OnMouseEnter", EventArgs.Empty);
            Call(region, "OnMouseLeave", EventArgs.Empty);
            Check(tip.Visible && tip.Text == settings.Text && tip.Parent!.ClientRectangle.Contains(tip.Bounds),
                "moving across the toolbar replaces the hint and keeps it within the preview");
            Call(settings, "OnMouseLeave", EventArgs.Empty);
            Check(!tip.Visible, "leaving the icon hides the hint immediately");
            Call(settings, "OnMouseEnter", EventArgs.Empty);
            Call(settings, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0));
            Call(settings, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 0, -1, -1, 0));
            Check(!tip.Visible, "pressing a toolbar icon dismisses the hint before the action opens");
        }
        finally
        {
            region.Shortcut = shortcut;
            Cursor.Position = main.PointToScreen(new Point(main.ClientSize.Width / 2, main.ClientSize.Height / 2));
            tip.HideHint();
        }
    }

    private static void Save(MainForm main, string output, string name)
    {
        Control surface = Find<Control>(main, "workspace");
        using Bitmap image = new(surface.Width, surface.Height);
        image.SetResolution(main.DeviceDpi, main.DeviceDpi);
        surface.DrawToBitmap(image, surface.ClientRectangle);
        image.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/preview-action-layout/" + main.DeviceDpi);
        Directory.CreateDirectory(output);
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        Size size = main.Size;
        bool motion = UiMotion.UserEnabled;
        try
        {
            Call(main, "ReleaseCaptureHotkeys");
            ApplicationPreferences working = original.Clone();
            working.AnimationsEnabled = false;
            foreach (string key in working.Shortcuts.Keys.ToArray()) working.Shortcuts[key] = Keys.None;
            main.GetType().GetField("preferences", Private)!.SetValue(main, working);
            Call(main, "RegisterCaptureHotkeys");
            Verify(main, "empty");
            Save(main, output, "empty");
            VerifyHints(main, output);
            Bitmap sample = new(600, 400);
            using (Graphics g = Graphics.FromImage(sample)) { g.Clear(Color.SteelBlue); g.FillRectangle(Brushes.PaleGoldenrod, 0, 260, 600, 140); }
            Call(main, "SetPreview", sample);
            await Task.Delay(80);
            Verify(main, "startup");
            foreach (LanguageMode language in new[] { LanguageMode.SimplifiedChinese, LanguageMode.TraditionalChinese, LanguageMode.English })
            foreach (SoftwareStyle style in new[] { SoftwareStyle.Default, SoftwareStyle.Claude })
            foreach (AppearanceMode appearance in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                working.Language = language;
                working.Appearance = appearance;
                working.Style = style;
                UiLanguage.Apply(language);
                Call(main, "ApplyWorkspaceLanguage");
                Call(main, "ApplyAppearance");
                foreach (bool minimum in new[] { false, true })
                {
                    main.Size = minimum ? main.MinimumSize : size;
                    await Task.Delay(60);
                    string context = $"{style}-{language}-{appearance}-{(minimum ? "minimum" : "normal")}";
                    Verify(main, context);
                    Find<Button>(main, "settingsButton").PerformClick();
                    Find<Button>(main, "cancelSettingsButton").PerformClick();
                    await Task.Delay(60);
                    Verify(main, context + "-after-settings");
                    Save(main, output, context);
                }
            }
            main.Size = main.MinimumSize;
            Button[] actions = Find<Control>(main, "previewActions").Controls.OfType<Button>().ToArray();
            // WinForms may dispose fonts currently assigned to controls during DPI scaling.
            Font[] fonts = actions.Select(button => (Font)button.Font.Clone()).ToArray();
            Font[] larger = fonts.Select(font => new Font(font.FontFamily, 16, font.Style)).ToArray();
            try
            {
                for (int i = 0; i < actions.Length; i++) actions[i].Font = larger[i];
                await Task.Delay(80);
                Verify(main, "large-text");
                Check(actions.Select(button => button.Top).Distinct().Count() == 1, "larger labels keep one row of icon commands");
                Save(main, output, "large-text");
            }
            finally
            {
                for (int i = 0; i < actions.Length; i++)
                {
                    Font restored = fonts[i];
                    actions[i].Font = restored;
                    actions[i].Disposed += (_, _) => restored.Dispose();
                }
                foreach (Font font in larger) font.Dispose();
            }
        }
        finally
        {
            Call(main, "CloseSettings");
            main.GetType().GetField("preferences", Private)!.SetValue(main, original);
            main.Size = size;
            UiLanguage.Apply(original.Language);
            UiMotion.UserEnabled = motion;
            Call(main, "ApplyWorkspaceLanguage");
            Call(main, "ApplyAppearance");
            Call(main, "RegisterCaptureHotkeys");
        }
        Console.WriteLine($"Preview action layout: {checks} checks passed.");
    }
}
