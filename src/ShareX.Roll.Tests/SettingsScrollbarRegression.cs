using ShareX.Roll;
using System.Drawing.Imaging;
using System.Reflection;
using System.IO;

internal static class SettingsScrollbarRegression
{
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Input(Control control, string method, object args) => control.GetType()
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, [args]);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private static Bitmap Render(Control control)
    {
        Bitmap bitmap = new(control.Width, control.Height);
        control.DrawToBitmap(bitmap, control.ClientRectangle);
        return bitmap;
    }
    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double F(byte n) { double v = n / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * F(c.R) + .7152 * F(c.G) + .0722 * F(c.B);
        }
        return (Math.Max(L(a), L(b)) + .05) / (Math.Min(L(a), L(b)) + .05);
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/settings-scrollbar");
        Directory.CreateDirectory(output);
        main.Size = main.MinimumSize;
        Find<Button>(main, "settingsButton").PerformClick();
        await Task.Delay(320);
        SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
        Panel viewport = Field<Panel>(page, "scroller");
        Control host = Find<Control>(page, "settingsScrollHost");
        Control bar = Find<Control>(page, "settingsScrollBar");
        TableLayoutPanel content = Field<TableLayoutPanel>(page, "content");
        int Offset() => -viewport.AutoScrollPosition.Y;
        int Maximum() => viewport.DisplayRectangle.Height - viewport.ClientSize.Height;
        foreach (AppearanceMode mode in new[] { AppearanceMode.Dark, AppearanceMode.Light, AppearanceMode.System, AppearanceMode.Dark })
        {
            Find<RadioButton>(page, "theme" + mode).Checked = true;
            await Task.Delay(60);
            viewport.AutoScrollPosition = Point.Empty;
            Find<Button>(page, "saveSettingsButton").Focus();
            CapturePalette palette = CapturePalette.For(mode);
            Check(bar.Visible && bar.Right <= host.ClientSize.Width && bar.Width >= 20 * main.DeviceDpi / 96,
                mode + ": scrollbar is inside the host with a 20px logical hit region");
            Check(viewport.ClientSize.Width == viewport.Parent!.ClientSize.Width && !viewport.HorizontalScroll.Visible,
                mode + ": native track is clipped without losing content width or adding horizontal scroll");
            using Bitmap image = Render(bar);
            Color thumb = image.GetPixel(image.Width / 2, Math.Min(image.Height - 1, (int)(12 * main.DeviceDpi / 96f)));
            Check(image.GetPixel(0, image.Height / 2).ToArgb() == palette.Background.ToArgb(), mode + ": track blends into page background");
            double contrast = Contrast(thumb, palette.Background);
            Check(contrast >= 3, $"{mode}: idle thumb contrast is {contrast:F2}:1");
            using Bitmap full = Render(main);
            full.Save(Path.Combine(output, $"settings-{mode}-{main.DeviceDpi}.png"), ImageFormat.Png);
            viewport.AutoScrollPosition = new Point(0, Maximum());
            using Bitmap bottom = Render(main);
            bottom.Save(Path.Combine(output, $"settings-bottom-{mode}-{main.DeviceDpi}.png"), ImageFormat.Png);
            viewport.AutoScrollPosition = Point.Empty;
        }
        int start = Offset();
        Input(viewport, "OnMouseWheel", new HandledMouseEventArgs(MouseButtons.None, 0, 10, 10, -120));
        Check(Offset() > start, "native viewport still scrolls with the mouse wheel");
        start = Offset();
        Input(bar, "OnMouseWheel", new HandledMouseEventArgs(MouseButtons.None, 0, 5, 5, -120));
        Check(Offset() > start, "wheel over the custom scrollbar scrolls the same viewport");
        bar.Focus();
        Input(bar, "OnKeyDown", new KeyEventArgs(Keys.End));
        Check(Offset() == Maximum(), "End reaches exact bottom");
        Control finalLink = Find<Control>(page, "aboutNotice");
        Check(viewport.RectangleToScreen(viewport.ClientRectangle).Contains(finalLink.RectangleToScreen(finalLink.ClientRectangle)), "software information links remain reachable at the bottom");
        Control last = Find<Control>(page, "scrollShortcut");
        Input(bar, "OnKeyDown", new KeyEventArgs(Keys.Home));
        Check(Offset() == 0, "Home reaches exact top");
        Input(bar, "OnKeyDown", new KeyEventArgs(Keys.PageDown));
        Check(Offset() > 0, "PageDown scrolls a page");
        Input(bar, "OnKeyDown", new KeyEventArgs(Keys.PageUp));
        Check(Offset() == 0, "PageUp returns to top");
        Input(bar, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, bar.Width / 2, bar.Height - 3, 0));
        Check(Offset() > 0, "track click scrolls a page");
        Input(bar, "OnKeyDown", new KeyEventArgs(Keys.Home));
        int thumbY = (int)(10 * main.DeviceDpi / 96f);
        Input(bar, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, bar.Width / 2, thumbY, 0));
        Input(bar, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, bar.Width / 2, bar.Height + 50, 0));
        Check(Offset() == Maximum(), "drag clamps at exact bottom even beyond the track");
        Input(bar, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, bar.Width / 2, bar.Height, 0));
        Check(!bar.Capture, "mouse release ends drag capture");
        Check(bar.AccessibilityObject.Role == AccessibleRole.ScrollBar && bar.AccessibilityObject.Value == "100%", "accessibility reports a vertical scrollbar and its value");
        bar.AccessibilityObject.Value = "0%";
        Check(Offset() == 0, "accessibility can set scroll position");
        viewport.ScrollControlIntoView(last);
        Check(Offset() > 0, "focus navigation can still bring an offscreen setting into view");
        // Exercise the no-overflow transition independently of available monitor height.
        content.Visible = false;
        content.AutoSize = false;
        content.Height = 1;
        viewport.AutoScrollPosition = Point.Empty;
        viewport.PerformLayout();
        host.PerformLayout();
        Check(!bar.Visible && viewport.Width == host.Width, "scrollbar disappears and releases its gutter when content fits");
        Find<Button>(page, "backToCaptureButton").PerformClick();
    }
}
