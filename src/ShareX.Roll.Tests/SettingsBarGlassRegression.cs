using ShareX.HelpersLib;
using ShareX.Roll;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class SettingsBarGlassRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static T Find<T>(Control parent, string name) where T : Control => (T)parent.Controls.Find(name, true).Single();
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Private)!.Invoke(target, null);
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private static void Capture(Control control, string path)
    {
        Rectangle area = control.RectangleToScreen(control.ClientRectangle);
        Point top = area.Location, bottom = new(area.Right, area.Bottom);
        LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref top);
        LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref bottom);
        nint previous = SetThreadDpiAwarenessContext((nint)(-4));
        try
        {
            using Bitmap bitmap = new(bottom.X - top.X, bottom.Y - top.Y, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(top, Point.Empty, bitmap.Size);
            bitmap.Save(path, ImageFormat.Png);
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }
    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath($"artifacts/settings-overlay/{main.DeviceDpi}");
        Directory.CreateDirectory(output);
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        Size originalSize = main.Size;
        bool topmost = main.TopMost;
        Point cursor = Cursor.Position;
        try
        {
            main.TopMost = true;
            main.ForceActivate();
            Cursor.Position = Point.Empty;
            Call(main, "ReleaseCaptureHotkeys");
            ApplicationPreferences working = original.Clone();
            working.TransparencyEnabled = working.AnimationsEnabled = true;
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, working);
            Call(main, "ApplyAppearance");
            Call(main, "OpenSettings");
            await Task.Delay(400);
            SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
            Panel viewport = Field<Panel>(page, "scroller");
            Control content = Field<Control>(page, "content");
            Control heading = Field<Control>(page, "heading");
            Control footer = Field<Control>(page, "footer");
            object top = Field<SettingsBarBackdrop>(page, "headingBackdrop");
            object bottom = Field<SettingsBarBackdrop>(page, "footerBackdrop");
            foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                Find<RadioButton>(page, "theme" + mode).Checked = true;
                foreach (bool maximized in new[] { false, true })
                {
                    main.WindowState = maximized ? FormWindowState.Maximized : FormWindowState.Normal;
                    if (!maximized) main.ClientSize = new Size(960 * main.DeviceDpi / 96, 720 * main.DeviceDpi / 96);
                    await Task.Delay(150);
                    Rectangle h = heading.Bounds, f = footer.Bounds;
                    Control host = Find<Control>(page, "settingsScrollHost");
                    Check(host.Bounds == page.ClientRectangle, mode + ": actual scroll viewport fills the page behind both fixed bars");
                    Check(page.GetChildAtPoint(new Point(page.Width / 2, (heading.Bottom + footer.Top) / 2)) == host &&
                        page.GetChildAtPoint(new Point(page.Width / 2, heading.Height / 2)) == heading,
                        mode + ": fixed sibling bars cover the scroll viewport while the options remain interactive");
                    foreach (int offset in new[] { 0, heading.Height, (viewport.DisplayRectangle.Height - viewport.Height) / 2 })
                    {
                        viewport.AutoScrollPosition = new Point(0, offset);
                        page.Refresh();
                        await Task.Delay(120);
                        string prefix = $"{mode}-{(maximized ? "max" : "normal")}-{offset}";
                        Capture(page, Path.Combine(output, prefix + ".png"));
                        using (Bitmap rendered = new(page.Width, page.Height))
                        {
                            page.DrawToBitmap(rendered, page.ClientRectangle);
                            rendered.Save(Path.Combine(output, prefix + "-render.png"), ImageFormat.Png);
                            BackdropSnapshot snapshot = BackdropSnapshot.Capture(page, rendered, CapturePalette.For(mode));
                            int x = heading.Left + heading.Width / 2;
                            int topY = Math.Max(0, heading.Padding.Top / 2), bottomY = page.Height - Math.Max(1, footer.Padding.Bottom / 2);
                            Check(snapshot.Pixels[topY * page.Width + x] == snapshot.Background &&
                                snapshot.Pixels[bottomY * page.Width + x] == snapshot.Background,
                                prefix + ": page-transition snapshots preserve the two foreground bars' material coverage");
                        }
                        foreach (var (owner, name) in new[] { (top, "top"), (bottom, "bottom") })
                        foreach (string field in new[] { "capture", "sample", "material" })
                            Field<Bitmap?>(owner, field)?.Save(Path.Combine(output, prefix + $"-{name}-{field}.png"), ImageFormat.Png);
                        Check(heading.Bounds == h && footer.Bounds == f, prefix + ": both fixed bars keep their geometry while scrolling");
                        Check(!viewport.HorizontalScroll.Visible, prefix + ": no horizontal scrolling");
                    }
                    Find<MotionToggle>(page, "transparencyToggle").Checked = false;
                    await Task.Delay(150);
                    Check(!Field<bool>(top, "enabled") && !Field<bool>(bottom, "enabled"), mode + ": transparency switch restores original solid bars");
                    Find<MotionToggle>(page, "transparencyToggle").Checked = true;
                    await Task.Delay(150);
                    foreach (string name in new[] { "languageSelector", "regionShortcut", "aboutRepository" })
                    {
                        Control target = Find<Control>(page, name);
                        viewport.ScrollControlIntoView(target);
                        target.Focus();
                        await Task.Delay(30);
                        Rectangle bounds = page.RectangleToClient(target.RectangleToScreen(target.ClientRectangle));
                        Check(bounds.Top >= heading.Bottom && bounds.Bottom <= footer.Top,
                            mode + "/" + name + ": focus scrolls into the unobscured area between the bars");
                    }
                    Check(page.Controls.Find("backToCaptureButton", true).Length == 0,
                        mode + ": the redundant header return button is removed");
                    footer.SelectNextControl(null, true, true, true, false);
                    typeof(SettingsPage).GetMethod("ProcessDialogKey", Private)!.Invoke(page, [Keys.Shift | Keys.Tab]);
                    Check(host.ContainsFocus, mode + ": Shift+Tab returns from the footer to the settings");
                    footer.SelectNextControl(null, false, true, true, false);
                    typeof(SettingsPage).GetMethod("ProcessDialogKey", Private)!.Invoke(page, [Keys.Tab]);
                    Check(host.ContainsFocus, mode + ": Tab wraps from the footer to the settings without a redundant header stop");
                }
                // A controlled content color change must reach the top bar. This distinguishes
                // internal content translucency from merely showing the desktop's Acrylic.
                viewport.AutoScrollPosition = new Point(0, heading.Height);
                Label probe = content.Controls.OfType<FlowLayoutPanel>().First().Controls.OfType<Label>().First();
                Color originalColor = probe.BackColor;
                try
                {
                    probe.BackColor = Color.OrangeRed;
                    page.Refresh();
                    await Task.Delay(100);
                    Point point = heading.PointToClient(probe.PointToScreen(new Point(probe.Width / 2, probe.Height / 2)));
                    Color red = Field<Bitmap>(top, "material").GetPixel(Math.Clamp(point.X, 0, heading.Width - 1), Math.Clamp(point.Y, 0, heading.Height - 1));
                    probe.BackColor = Color.RoyalBlue;
                    page.Refresh();
                    await Task.Delay(100);
                    Color blue = Field<Bitmap>(top, "material").GetPixel(Math.Clamp(point.X, 0, heading.Width - 1), Math.Clamp(point.Y, 0, heading.Height - 1));
                    Console.WriteLine($"PROBE {mode}: red={red}; blue={blue}");
                    Check(red.R - red.B > blue.R - blue.B + 3, mode + ": scrolling settings content contributes to the bar background");
                }
                finally { probe.BackColor = originalColor; page.Refresh(); }
            }
        }
        finally
        {
            main.WindowState = FormWindowState.Normal;
            Call(main, "CloseSettings");
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, original);
            main.Size = originalSize;
            Call(main, "ApplyAppearance");
            Call(main, "RegisterCaptureHotkeys");
            main.TopMost = topmost;
            Cursor.Position = cursor;
        }
    }
}
