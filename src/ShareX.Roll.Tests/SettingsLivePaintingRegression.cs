using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class SettingsLivePaintingRegression
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogicalToPhysicalPointForPerMonitorDPI(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    private static Rectangle PhysicalArea(Control control)
    {
        Rectangle area = control.RectangleToScreen(control.ClientRectangle);
        NativePoint top = new() { X = area.Left, Y = area.Top }, bottom = new() { X = area.Right, Y = area.Bottom };
        Check(LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref top) && LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref bottom), "screen sampling uses physical coordinates for this window");
        return Rectangle.FromLTRB(top.X, top.Y, bottom.X, bottom.Y);
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private static Bitmap ScreenImage(Rectangle area)
    {
        IntPtr previous = SetThreadDpiAwarenessContext((IntPtr)(-4));
        try
        {
            Bitmap image = new(area.Width, area.Height, PixelFormat.Format24bppRgb);
            using Graphics graphics = Graphics.FromImage(image);
            graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
            return image;
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/settings-live-painting");
        Directory.CreateDirectory(output);
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        bool oldTopMost = main.TopMost;
        try
        {
            main.TopMost = true;
            main.ForceActivate();
            foreach (AppearanceMode mode in new[] { AppearanceMode.Dark, AppearanceMode.Light })
            {
                ApplicationPreferences preferences = original.Clone();
                preferences.Appearance = mode;
                preferences.AnimationsEnabled = true;
                Set(main, "preferences", preferences);
                Call(main, "ApplyAppearance");
                Find<Button>(main, "settingsButton").PerformClick();
                await Task.Delay(320);
                SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
                SettingsChoice language = Find<SettingsChoice>(page, "languageSelector");
                SettingsDisclosure header = Find<SettingsDisclosure>(page, "scrollingOptionsButton");
                Rectangle languageArea = PhysicalArea(language);
                using Bitmap reference = ScreenImage(languageArea);
                List<Bitmap> frames = new();
                using ManualResetEventSlim started = new();
                Task sample = Task.Run(() =>
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    while (timer.ElapsedMilliseconds < 480)
                    {
                        frames.Add(ScreenImage(languageArea));
                        started.Set();
                        Thread.Sleep(5);
                    }
                });
                await Task.Run(() => started.Wait());
                header.PerformClick();
                await sample;
                try
                {
                    int changed = 0, largest = 0;
                    int scale = Math.Max(1, (int)Math.Round(main.DeviceDpi / 96d * reference.Width / language.Width));
                    for (int index = 0; index < frames.Count; index++)
                    {
                        int pixels = 0;
                        for (int y = 6 * scale; y < reference.Height - 6 * scale; y++)
                        for (int x = 12 * scale; x < reference.Width - 34 * scale; x++)
                        {
                            Color before = reference.GetPixel(x, y), after = frames[index].GetPixel(x, y);
                            if (Math.Max(Math.Abs(before.R - after.R), Math.Max(Math.Abs(before.G - after.G), Math.Abs(before.B - after.B))) > 12) pixels++;
                        }
                        largest = Math.Max(largest, pixels);
                        if (pixels > 5) { changed++; frames[index].Save(Path.Combine(output, $"language-change-{mode}-{index}-{main.DeviceDpi}.png"), ImageFormat.Png); }
                    }
                    Check(changed == 0, $"{mode}: language text and fill stay stable across {frames.Count} real-screen frames (largest changed pixel count {largest})");
                }
                finally { foreach (Bitmap frame in frames) frame.Dispose(); }
                CapturePalette palette = CapturePalette.For(mode);
                foreach (string name in new[] { "nudStartDelay", "nudScrollDelay", "nudScrollAmount" })
                {
                    NumericUpDown number = Find<NumericUpDown>(page, name);
                    SettingsNumberField field = (SettingsNumberField)number.Parent!;
                    Field<Panel>(page, "scroller").ScrollControlIntoView(field);
                    await Task.Delay(40);
                    using Bitmap image = ScreenImage(PhysicalArea(field));
                    image.Save(Path.Combine(output, $"numeric-{name}-{mode}-{main.DeviceDpi}.png"), ImageFormat.Png);
                    int sampleX = number.Right - (int)(2 * main.DeviceDpi / 96f);
                    int sampleY = number.Top + number.Height / 2;
                    Color actual = image.GetPixel(sampleX * image.Width / field.Width, sampleY * image.Height / field.Height);
                    Console.WriteLine($"COLOR: {mode}/{name}: actual={actual}, expected={palette.Canvas}, native={number.BackColor}");
                    Check(actual.ToArgb() == palette.Canvas.ToArgb(), $"{mode}/{name}: unused native spin area has the same fill as the rounded field");
                }
                Find<Button>(page, "cancelSettingsButton").PerformClick();
            }
        }
        finally
        {
            Call(main, "CloseSettings");
            Set(main, "preferences", original);
            Call(main, "ApplyAppearance");
            main.TopMost = oldTopMost;
        }
    }
}
