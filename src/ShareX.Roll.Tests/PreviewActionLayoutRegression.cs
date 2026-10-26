using ShareX.Roll;
using ShareX.Roll.Localization;
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
        Control actionPanel = Find<Control>(main, "previewActions");
        Console.WriteLine($"LAYOUT: {context}: toolbar={toolbar.Bounds}, actions={actionPanel.Bounds}, max={actionPanel.MaximumSize}, preferred={actionPanel.PreferredSize}");
        foreach (string name in new[] { "editButton", "copyButton", "saveButton", "settingsButton" })
        {
            Button button = Find<Button>(main, name);
            float scale = button.DeviceDpi / 96f;
            // Verify the actual drawable label slot, rather than only the native Button bounds.
            int labelSlot = (int)ButtonElevation.FaceBounds(button.ClientSize, scale).Width -
                (int)Math.Round(24 * scale) - (int)Math.Round(16 * scale) - (int)Math.Round(8 * scale);
            using Graphics graphics = button.CreateGraphics();
            int textWidth = TextRenderer.MeasureText(graphics, button.Text, button.Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            Console.WriteLine($"LABEL: {context}/{name}: {button.Text}, native={button.Bounds}, drawable={labelSlot}, needed={textWidth}, DPI={button.DeviceDpi}");
            Check(labelSlot >= textWidth + Math.Max(1, (int)scale), context + "/" + name + ": complete label fits beside its icon inside the visible face");
            Check(ButtonElevation.FaceBounds(button.ClientSize, scale).Height - TextRenderer.MeasureText(graphics, "Ag", button.Font).Height >= 16 * scale,
                context + "/" + name + ": compact face leaves at least 8 logical pixels above and below the text");
            if (name != "settingsButton")
                Check(toolbar.RectangleToScreen(toolbar.ClientRectangle).Contains(button.RectangleToScreen(button.ClientRectangle)),
                    context + "/" + name + ": the complete native hit target stays inside the toolbar");
        }
        Control actions = Find<Control>(main, "previewActions");
        Button[] buttons = actions.Controls.OfType<Button>().ToArray();
        Check(buttons.All(button => actions.ClientRectangle.Contains(button.Bounds)) &&
            buttons.All(button => buttons.All(other => button == other || !button.Bounds.IntersectsWith(other.Bounds))),
            context + ": actions fit without overlap, including wrapped rows");
    }

    private static void Save(MainForm main, string output, string name)
    {
        Control surface = Find<Control>(main, "previewSurface");
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
            Call(main, "SetPreview", new Bitmap(600, 400));
            await Task.Delay(80);
            Verify(main, "startup");
            foreach (LanguageMode language in new[] { LanguageMode.SimplifiedChinese, LanguageMode.TraditionalChinese, LanguageMode.English })
            foreach (AppearanceMode appearance in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                working.Language = language;
                working.Appearance = appearance;
                UiLanguage.Apply(language);
                Call(main, "ApplyWorkspaceLanguage");
                Call(main, "ApplyAppearance");
                foreach (bool minimum in new[] { false, true })
                {
                    main.Size = minimum ? main.MinimumSize : size;
                    await Task.Delay(60);
                    string context = $"{language}-{appearance}-{(minimum ? "minimum" : "normal")}";
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
            Font[] fonts = actions.Select(button => button.Font).ToArray();
            Font[] larger = fonts.Select(font => new Font(font.FontFamily, 16, font.Style)).ToArray();
            try
            {
                for (int i = 0; i < actions.Length; i++) actions[i].Font = larger[i];
                await Task.Delay(80);
                Verify(main, "large-text");
                Check(actions.Select(button => button.Top).Distinct().Count() > 1, "larger labels wrap to another row instead of being shortened");
                Save(main, output, "large-text");
            }
            finally
            {
                for (int i = 0; i < actions.Length; i++) actions[i].Font = fonts[i];
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
