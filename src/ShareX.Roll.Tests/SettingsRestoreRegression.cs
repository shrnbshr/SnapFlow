using ShareX.HelpersLib;
using ShareX.Roll;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class SettingsRestoreRegression
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Private)!.Invoke(target, null);
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    private static Bitmap ScreenImage(Control control)
    {
        Rectangle bounds = control.RectangleToScreen(control.ClientRectangle);
        NativePoint top = new() { X = bounds.Left, Y = bounds.Top }, bottom = new() { X = bounds.Right, Y = bounds.Bottom };
        LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref top);
        LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref bottom);
        IntPtr previous = SetThreadDpiAwarenessContext((IntPtr)(-4));
        try
        {
            Bitmap image = new(bottom.X - top.X, bottom.Y - top.Y, PixelFormat.Format24bppRgb);
            using Graphics graphics = Graphics.FromImage(image);
            graphics.CopyFromScreen(new Point(top.X, top.Y), Point.Empty, image.Size);
            return image;
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private static void Describe(SettingsPage page, string context)
    {
        Panel viewport = Field<Panel>(page, "scroller");
        Control content = Field<Control>(page, "content");
        Console.WriteLine($"GEOMETRY {context}: page={page.Bounds}; heading={Field<Control>(page, "heading").Bounds}; footer={Field<Control>(page, "footer").Bounds}; host={Find<Control>(page, "settingsScrollHost").Bounds}; viewport={viewport.Bounds}; display={viewport.DisplayRectangle}; offset={viewport.AutoScrollPosition}; content={content.Bounds}");
        foreach (string name in new[] { "transparencyRow", "motionRow", "languageRow", "saveLocationOptions", "saveLocationGroup", "scrollingOptionsRow", "shortcutList" })
        {
            Control row = Find<Control>(page, name);
            Console.WriteLine($"ROW {context}/{name}: {row.Bounds}; margin={row.Margin}; parent={row.Parent!.Name}");
        }
    }

    internal static async Task RunAsync(MainForm main, bool baseline)
    {
        string output = Path.GetFullPath("artifacts/settings-wide-layout/restore/" + main.DeviceDpi);
        Directory.CreateDirectory(output);
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        bool topmost = main.TopMost;
        Size size = main.Size;
        Point mouse = Cursor.Position;
        try
        {
            main.TopMost = true;
            main.ForceActivate();
            InputHelpers.SendMouseMove(new Point(main.Left - 20, main.Top));
            Call(main, "ReleaseCaptureHotkeys");
            foreach (bool maximized in new[] { false, true })
            foreach (bool glass in new[] { true, false })
            {
                main.WindowState = maximized ? FormWindowState.Maximized : FormWindowState.Normal;
                if (!maximized) main.Size = size;
                Cursor.Position = Point.Empty;
                ApplicationPreferences working = original.Clone();
                working.Appearance = AppearanceMode.Dark;
                working.AnimationsEnabled = true;
                working.TransparencyEnabled = glass;
                working.UseDefaultSaveFolder = false;
                typeof(MainForm).GetField("preferences", Private)!.SetValue(main, working);
                Call(main, "ApplyAppearance");
                Call(main, "OpenSettings");
                await Task.Delay(400);
                SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
                Panel viewport = Field<Panel>(page, "scroller");
                Control content = Field<Control>(page, "content");
                Describe(page, glass + "/initial");
                foreach (bool expanded in new[] { false, true })
                {
                    if (expanded) { Find<Button>(page, "scrollingOptionsButton").PerformClick(); await Task.Delay(300); }
                    foreach (double progress in new[] { 0, .28, .8 })
                    {
                        string context = $"{(maximized ? "maximized" : "normal")}-glass-{glass}-expanded-{expanded}-scroll-{progress}";
                        viewport.AutoScrollPosition = new Point(0, (int)((viewport.DisplayRectangle.Height - viewport.ClientSize.Height) * progress));
                        // Establish a fully painted reference after this synthetic native scroll.
                        viewport.Invalidate(true);
                        viewport.Update();
                        await Task.Delay(100);
                        Rectangle geometry = content.Bounds;
                        Point offset = viewport.AutoScrollPosition;
                        Size viewportSize = viewport.ClientSize;
                        using Bitmap before = ScreenImage(viewport);
                        before.Save(Path.Combine(output, context + "-before.png"), ImageFormat.Png);
                        main.WindowState = FormWindowState.Minimized;
                        await Task.Delay(150);
                        Describe(page, context + "/minimized");
                        main.WindowState = maximized ? FormWindowState.Maximized : FormWindowState.Normal;
                        main.ForceActivate();
                        await Task.Delay(350);
                        Describe(page, context + "/restored");
                        using Bitmap after = ScreenImage(viewport);
                        after.Save(Path.Combine(output, context + "-after.png"), ImageFormat.Png);
                        int changed = 0, samples = 0;
                        if (before.Size == after.Size)
                            for (int y = 8; y < before.Height - 8; y += 4)
                            for (int x = 8; x < before.Width - 8; x += 4)
                            {
                                Color a = before.GetPixel(x, y), b = after.GetPixel(x, y);
                                if (Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))) > 24) changed++;
                                samples++;
                            }
                        Console.WriteLine($"PIXELS {context}: changed={changed}/{samples}");
                        if (baseline) continue;
                        Check(content.Bounds == geometry && viewport.AutoScrollPosition == offset && viewport.ClientSize == viewportSize,
                            context + ": minimize and restore preserve content geometry and scroll position");
                        Check(samples > 0 && changed < samples * .01,
                            context + ": restored native controls remain painted across the viewport");
                    }
                }
                Call(main, "CloseSettings");
                await Task.Delay(350);
            }
        }
        finally
        {
            main.WindowState = FormWindowState.Normal;
            Call(main, "CloseSettings");
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, original);
            main.Size = size;
            Call(main, "ApplyAppearance");
            Call(main, "RegisterCaptureHotkeys");
            main.TopMost = topmost;
            Cursor.Position = mouse;
        }
    }
}
