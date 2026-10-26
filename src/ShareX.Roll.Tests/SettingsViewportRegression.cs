using ShareX.HelpersLib;
using ShareX.Roll;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class SettingsViewportRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Private)!.Invoke(target, null);
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private static void Capture(Control control, string path)
    {
        Rectangle bounds = control.RectangleToScreen(control.ClientRectangle);
        Point top = bounds.Location, bottom = new(bounds.Right, bounds.Bottom);
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

    internal static async Task RunAsync(MainForm main, bool baseline)
    {
        string output = Path.GetFullPath($"artifacts/settings-viewport/{(baseline ? "before" : "after")}/{main.DeviceDpi}");
        Directory.CreateDirectory(output);
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        Size size = main.Size;
        Point location = main.Location, cursor = Cursor.Position;
        FormBorderStyle border = main.FormBorderStyle;
        bool topmost = main.TopMost;
        try
        {
            main.TopMost = true;
            main.ForceActivate();
            Cursor.Position = new Point(0, 0);
            Call(main, "ReleaseCaptureHotkeys");
            ApplicationPreferences working = original.Clone();
            working.AnimationsEnabled = true;
            working.TransparencyEnabled = true;
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, working);
            Call(main, "ApplyAppearance");
            Call(main, "OpenSettings");
            await Task.Delay(450);
            SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
            Panel viewport = Field<Panel>(page, "scroller");
            Control content = Field<Control>(page, "content");
            Control host = Find<Control>(page, "settingsScrollHost");
            Control bar = Find<Control>(page, "settingsScrollBar");
            Control heading = Field<Control>(page, "heading");
            Control footer = Field<Control>(page, "footer");
            foreach (AppearanceMode theme in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                Find<RadioButton>(page, "theme" + theme).Checked = true;
                foreach (string state in new[] { "minimum", "normal", "maximized", "restored", "fullscreen", "restored-fullscreen" })
                {
                    if (state == "minimum") main.Size = main.MinimumSize;
                    if (state == "normal") main.ClientSize = new Size(960 * main.DeviceDpi / 96, 720 * main.DeviceDpi / 96);
                    if (state == "maximized") main.WindowState = FormWindowState.Maximized;
                    if (state == "restored") main.WindowState = FormWindowState.Normal;
                    if (state == "fullscreen") { main.FormBorderStyle = FormBorderStyle.None; main.Bounds = Screen.FromControl(main).Bounds; }
                    if (state == "restored-fullscreen") { main.FormBorderStyle = border; main.ClientSize = new Size(960 * main.DeviceDpi / 96, 720 * main.DeviceDpi / 96); }
                    if (main.WindowState == FormWindowState.Normal && state != "fullscreen")
                    {
                        Rectangle area = Screen.FromControl(main).WorkingArea;
                        main.Location = new Point(area.Left + (area.Width - main.Width) / 2, area.Top + (area.Height - main.Height) / 2);
                    }
                    Find<Button>(page, "saveSettingsButton").Focus();
                    viewport.AutoScrollPosition = Point.Empty;
                    // Synthetic AutoScrollPosition moves do not run the normal wheel
                    // paint cycle. Establish a painted reference before screen sampling.
                    viewport.Invalidate(true);
                    viewport.Update();
                    await Task.Delay(350);
                    string context = $"{theme}-{state}";
                    Point track = page.PointToClient(bar.PointToScreen(new Point(bar.Width, 0)));
                    Point contentOrigin = page.PointToClient(content.PointToScreen(Point.Empty));
                    Console.WriteLine($"GEOMETRY {context}: dpi={main.DeviceDpi}; page={page.ClientSize}; host={host.Bounds}; trackRight={track.X}; content={content.Bounds}; contentOrigin={contentOrigin}; heading={heading.Bounds}; hscroll={viewport.HorizontalScroll.Visible}");
                    Capture(main, Path.Combine(output, context + ".png"));
                    int gap = (int)(28 * main.DeviceDpi / 96f);
                    int capped = Math.Min(page.ClientSize.Width - gap * 2, (int)(760 * main.DeviceDpi / 96f));
                    if (!baseline)
                    {
                        Check(Math.Abs(page.ClientSize.Width - track.X) <= 1, context + ": scrollbar follows the page viewport right edge without an extra outer gutter");
                        Check(heading.Width - heading.Padding.Horizontal == capped && footer.Width - footer.Padding.Horizontal == capped &&
                            heading.Padding.Left == footer.Padding.Left &&
                            Math.Abs(heading.Left + heading.Padding.Left - (page.Width - capped) / 2) <= 1,
                            context + ": heading and footer keep the original centered reading column");
                        Check(heading.Left == 0 && footer.Left == 0 && heading.Right == bar.Left && footer.Right == bar.Left &&
                            page.GetChildAtPoint(new Point(bar.Left + bar.Width / 2, heading.Height / 2)) == host,
                            context + ": glass spans both viewport edges without blocking the scrollbar");
                        Check(content.Width == capped - (bar.Visible ? bar.Width : 0) &&
                            Math.Abs(contentOrigin.X - (page.Width - capped) / 2) <= 1,
                            context + ": content keeps its reading width and alignment");
                    }
                    Check(!viewport.HorizontalScroll.Visible && viewport.AutoScrollPosition.X == 0, context + ": no horizontal scroll or clipping");
                    Check(UiMotion.UserEnabled && Field<MotionToggle>(page, "transparency").Checked,
                        context + ": animation and transparency remain enabled");
                    Find<Button>(page, "scrollingOptionsButton").PerformClick();
                    await Task.Delay(300);
                    Check(!viewport.HorizontalScroll.Visible, context + ": expanded settings fit the reading column");
                    viewport.AutoScrollPosition = new Point(0, viewport.DisplayRectangle.Height);
                    await Task.Delay(100);
                    Control last = Find<Control>(page, "aboutNotice");
                    Check(viewport.RectangleToScreen(viewport.ClientRectangle).Contains(last.RectangleToScreen(last.ClientRectangle)),
                        context + ": bottom content stays reachable");
                    Find<Button>(page, "scrollingOptionsButton").PerformClick();
                    await Task.Delay(300);
                }
            }
        }
        finally
        {
            main.WindowState = FormWindowState.Normal;
            main.FormBorderStyle = border;
            Call(main, "CloseSettings");
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, original);
            main.Size = size;
            main.Location = location;
            Call(main, "ApplyAppearance");
            Call(main, "RegisterCaptureHotkeys");
            main.TopMost = topmost;
            Cursor.Position = cursor;
        }
    }
}
