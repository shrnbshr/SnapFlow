using ShareX.Roll;
using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.Reflection;
using System.IO;

internal static class ThemeBackgroundRegression
{
    private static readonly string Output = Path.GetFullPath("artifacts/theme-background-fix");
    private static T Find<T>(Control target, string name) where T : Control => (T)target.Controls.Find(name, true).Single();
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private static void Verify(SettingsPage page, CapturePalette palette)
    {
        foreach (SettingsGroup group in Descendants(page).OfType<SettingsGroup>())
        {
            List<string> differences = new();
            foreach (Control child in Descendants(group))
            {
                // Existing input wells use Canvas; captions and the rest of each card use Surface.
                Color expectedBackground = child is SettingsChoice or TextBoxBase or NumericUpDown || child.Parent is NumericUpDown
                    ? palette.Canvas : palette.Surface;
                if (child.BackColor != expectedBackground)
                    differences.Add($"{child.GetType().Name} {child.Name}: {child.BackColor} instead of {expectedBackground}");
                if (child is Label && child.ForeColor != (Equals(child.Tag, "secondary") ? palette.Secondary : palette.Text))
                    differences.Add("wrong text color: " + child.Text);
                if (child.Visible && child.Width > 0 && child.Height > 0
                    && (child is Label or ModeGlyph or MotionToggle || child is Button { Text: "×" }))
                {
                    using Bitmap bitmap = new(child.Width, child.Height);
                    child.DrawToBitmap(bitmap, new Rectangle(Point.Empty, child.Size));
                    if (bitmap.GetPixel(0, 0).ToArgb() != palette.Surface.ToArgb())
                        differences.Add("visible rectangular background: " + child.GetType().Name + " " + child.Name);
                }
            }
            Check(differences.Count == 0, group.Name + " keeps consistent surface and text colors after real theme selection: " + string.Join("; ", differences));
        }
    }

    internal static async Task RunAsync(MainForm main)
    {
        Directory.CreateDirectory(Output);
        main.TopMost = true;
        main.ForceActivate();
        try
        {
            Find<Button>(main, "settingsButton").PerformClick();
            SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
            int step = 0;
            foreach (AppearanceMode mode in new[] { AppearanceMode.Dark, AppearanceMode.Light, AppearanceMode.Dark, AppearanceMode.System })
            {
                // Use the actual selection path; do not repair colors in the test.
                Find<RadioButton>(page, "theme" + mode).Checked = true;
                await Task.Delay(280);
                using (Bitmap bitmap = new(main.Width, main.Height))
                {
                    main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, main.Size));
                    bitmap.Save(Path.Combine(Output, $"settings-{step}-{mode}-{main.DeviceDpi}.png"), ImageFormat.Png);
                }
                if (main.DeviceDpi > 96)
                {
                    using Bitmap bitmap = new Screenshot().CaptureWindow(main.Handle);
                    bitmap.Save(Path.Combine(Output, $"settings-live-{step}-{mode}.png"), ImageFormat.Png);
                }
                Verify(page, CapturePalette.For(mode));
                typeof(MainForm).GetMethod("ApplyAppearance", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
                Verify(page, CapturePalette.For(mode));
                step++;
            }
            Find<Button>(page, "cancelSettingsButton").PerformClick();
            await Task.Delay(260);
            Check(!main.Controls.Find("SettingsPage", true).Any(control => control.Visible), "return restores the capture workspace and hides the cached settings page");
        }
        finally
        {
            typeof(MainForm).GetMethod("CloseSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
            main.TopMost = false;
        }
    }
}
