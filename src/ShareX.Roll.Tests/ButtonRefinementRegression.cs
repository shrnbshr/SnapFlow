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
    private static (int Top, int Bottom) Plate(Bitmap image, Color face)
    {
        int x = image.Width / 2;
        int top = 0, bottom = image.Height - 1;
        while (top < image.Height && image.GetPixel(x, top).ToArgb() != face.ToArgb()) top++;
        while (bottom > top && image.GetPixel(x, bottom).ToArgb() != face.ToArgb()) bottom--;
        return (top, bottom);
    }
    private static bool FocusRing(Bitmap image, CapturePalette palette, float scale)
    {
        RectangleF face = ButtonElevation.FaceBounds(image.Size, scale);
        for (int y = (int)(face.Top - 2 * scale); y < (int)(face.Top + 4 * scale); y++)
            if (image.GetPixel(image.Width / 2, y).ToArgb() == palette.Accent.ToArgb()) return true;
        return false;
    }
    private static Rectangle TextInk(Bitmap image, CapturePalette palette)
    {
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 3 && Math.Abs(a.G - b.G) <= 3 && Math.Abs(a.B - b.B) <= 3;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                Color pixel = image.GetPixel(x, y);
                if (!Near(pixel, palette.Text) && !Near(pixel, palette.Secondary)) continue;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
        return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
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
                for (int i = 0; i < buttons.Length; i++)
                {
                    CaptureModeButton button = buttons[i];
                    Check(button.Height >= 72 * scale, mode + ": capture action has room for its taller hover plate");
                    if (i > 0) Check(button.Top - buttons[i - 1].Top == 88 * scale, mode + ": capture actions use the compact 88px row rhythm");
                    UiTween hover = Field<UiTween>(button, "hover");
                    Rectangle bounds = button.Bounds;
                    hover.SnapTo(.2f);
                    using Bitmap partial = Render(button);
                    using Bitmap partialMask = new(button.Width, button.Height);
                    using (Graphics g = Graphics.FromImage(partialMask)) CaptureButtonPainter.PaintBackdrop(button, g);
                    hover.SnapTo(1);
                    using Bitmap full = Render(button);
                    RectangleF face = ButtonElevation.FaceBounds(button.ClientSize, scale);
                    Rectangle text = TextInk(full, palette);
                    Check(!text.IsEmpty && text.Top - face.Top >= 12 * scale && face.Bottom - text.Bottom >= 12 * scale,
                        mode + ": title and shortcut retain compact padding above and below");
                    Check(face.Right - text.Right >= 12 * scale,
                        mode + ": text keeps at least 12 logical pixels from the right edge");
                    using Bitmap fullMask = new(button.Width, button.Height);
                    using (Graphics g = Graphics.FromImage(fullMask)) CaptureButtonPainter.PaintBackdrop(button, g);
                    var first = Plate(partialMask, CaptureButtonPainter.Coverage(.8f));
                    var last = Plate(fullMask, Color.Black);
                    if (UiMotion.Enabled)
                        Check(last.Top < first.Top && last.Bottom > first.Bottom, mode + ": hover plate expands above and below its center");
                    Check(button.Bounds == bounds, mode + ": hover keeps the native click target fixed");
                    full.Save(Path.Combine(output, $"{mode}-{button.Kind}-hover-{main.DeviceDpi}.png"), ImageFormat.Png);
                    hover.SnapTo(0);
                }
                Field<UiTween>(buttons[0], "hover").SnapTo(1);
                using (Bitmap mainImage = Render(main)) mainImage.Save(Path.Combine(output, $"main-{mode}-{main.DeviceDpi}.png"), ImageFormat.Png);
                TableLayoutPanel sidebar = Field<TableLayoutPanel>(main, "captureSidebar");
                using (Bitmap sidebarImage = Render(sidebar))
                using (Bitmap crop = sidebarImage.Clone(new Rectangle(0, 0, sidebar.Width,
                    Math.Min(sidebar.Height, buttons[^1].Bottom + (int)(12 * scale))), PixelFormat.Format32bppArgb))
                    crop.Save(Path.Combine(output, $"sidebar-{mode}-{main.DeviceDpi}.png"), ImageFormat.Png);
                Field<UiTween>(buttons[0], "hover").SnapTo(0);
                main.Size = main.MinimumSize;
                Application.DoEvents();
                foreach (CaptureModeButton button in buttons)
                {
                    Rectangle screen = button.RectangleToScreen(button.ClientRectangle);
                    Check(main.RectangleToScreen(main.ClientRectangle).Contains(screen) &&
                        button.Parent!.ClientRectangle.Contains(button.Bounds), mode + $": all three capture actions fit the minimum window and their parent ({button.Name} {button.Bounds}, parent {button.Parent!.ClientSize})");
                    Check(ButtonElevation.FaceBounds(button.ClientSize, scale).Height >= 64 * scale,
                        mode + ": the minimum window retains the compact two-line card height");
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
