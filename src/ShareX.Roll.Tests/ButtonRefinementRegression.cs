using ShareX.HelpersLib;
using ShareX.Roll;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

internal static class ButtonRefinementRegression
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
        Console.WriteLine("PASS: " + message);
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static T Find<T>(Control target, string name) where T : Control => (T)target.Controls.Find(name, true).Single();
    private static Bitmap Render(Control control)
    {
        Bitmap image = new(control.Width, control.Height);
        image.SetResolution(control.DeviceDpi, control.DeviceDpi);
        control.DrawToBitmap(image, control.ClientRectangle);
        return image;
    }
    private static bool FocusRing(Bitmap image, CapturePalette palette, float scale)
    {
        for (int y = 1; y < (int)(8 * scale); y++)
            if (image.GetPixel(image.Width / 2, y).ToArgb() == palette.Accent.ToArgb()) return true;
        return false;
    }

    internal static void Run()
    {
        string output = Path.GetFullPath("artifacts/button-refinement");
        Directory.CreateDirectory(output);
        using MainForm main = new();
        main.Show();
        Application.DoEvents();
        ApplicationPreferences preferences = Field<ApplicationPreferences>(main, "preferences");
        AppearanceMode original = preferences.Appearance;
        bool motion = preferences.AnimationsEnabled;
        try
        {
            preferences.AnimationsEnabled = true;
            float scale = main.DeviceDpi / 96f;
            foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                preferences.Appearance = mode;
                Call(main, "ApplyAppearance");
                NativeMethods.SendMessage(main.Handle, (uint)WindowsMessages.CHANGEUISTATE, new IntPtr((1 << 16) | 1), IntPtr.Zero);
                CapturePalette palette = CapturePalette.For(mode);
                main.ClientSize = new Size((int)(860 * scale), (int)(680 * scale));
                Application.DoEvents();
                CaptureModeButton[] buttons = ApplicationPreferences.Actions.Select(action => Find<CaptureModeButton>(main, action.Kind + "Button")).ToArray();
                Check(buttons.Select(button => button.Top).Distinct().Count() == 1,
                    mode + ": capture commands occupy one row");
                foreach (CaptureModeButton button in buttons)
                {
                    Check(button.Height == Math.Round(44 * scale), mode + ": capture command scales to 44 logical pixels");
                    UiTween hover = Field<UiTween>(button, "hover");
                    Rectangle bounds = button.Bounds;
                    hover.SnapTo(0);
                    using Bitmap idle = Render(button);
                    hover.SnapTo(1);
                    using Bitmap active = Render(button);
                    Point sample = new((int)Math.Round(8 * scale), button.Height / 2);
                    Check(idle.GetPixel(sample.X, sample.Y).ToArgb() == palette.Background.ToArgb() &&
                        active.GetPixel(sample.X, sample.Y).ToArgb() == palette.Hover.ToArgb(),
                        mode + ": hover reveals the compact button surface");
                    Check(button.Bounds == bounds, mode + ": hover keeps the native click target fixed");
                    active.Save(Path.Combine(output, $"{mode}-{button.Kind}-hover-{main.DeviceDpi}.png"), ImageFormat.Png);
                    hover.SnapTo(0);
                }
                Field<UiTween>(buttons[0], "hover").SnapTo(1);
                using (Bitmap mainImage = Render(main)) mainImage.Save(Path.Combine(output, $"main-{mode}-{main.DeviceDpi}.png"), ImageFormat.Png);
                Field<UiTween>(buttons[0], "hover").SnapTo(0);
                main.Size = main.MinimumSize;
                Application.DoEvents();
                foreach (CaptureModeButton button in buttons)
                {
                    Rectangle screen = button.RectangleToScreen(button.ClientRectangle);
                    Check(main.RectangleToScreen(main.ClientRectangle).Contains(screen) &&
                        button.Parent!.ClientRectangle.Contains(button.Bounds),
                        mode + ": all capture commands remain visible at minimum size");
                }
                Button settings = Find<Button>(main, "settingsButton");
                NativeMethods.SendMessage(main.Handle, (uint)WindowsMessages.CHANGEUISTATE, new IntPtr((1 << 16) | 1), IntPtr.Zero);
                settings.Focus();
                using (Bitmap mouseFocus = Render(settings)) Check(!FocusRing(mouseFocus, palette, scale), "mouse focus does not draw a decorative outline");
                NativeMethods.SendMessage(main.Handle, (uint)WindowsMessages.CHANGEUISTATE, new IntPtr((1 << 16) | 2), IntPtr.Zero);
                using (Bitmap keyboardFocus = Render(settings)) Check(FocusRing(keyboardFocus, palette, scale), "Windows keyboard focus cues remain visible");
            }
            Console.WriteLine($"Button refinement: {checks} checks passed.");
        }
        finally
        {
            preferences.Appearance = original;
            preferences.AnimationsEnabled = motion;
            Call(main, "ApplyAppearance");
            main.Close();
        }
    }
}
