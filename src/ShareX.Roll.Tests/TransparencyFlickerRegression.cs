using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class TransparencyFlickerRegression
{
    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Call(MainForm main, string name) => typeof(MainForm)
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    private sealed class Background : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override void OnPaint(PaintEventArgs e) => e.Graphics.Clear(Color.RoyalBlue);
    }
    internal static async Task RunAsync(MainForm main, bool baseline)
    {
        var original = Field<ApplicationPreferences>(main, "preferences");
        bool originalTop = main.TopMost;
        Point originalCursor = Cursor.Position;
        using Background background = new() { StartPosition = FormStartPosition.Manual,
            Bounds = Rectangle.Inflate(main.Bounds, 40, 40), ShowInTaskbar = false };
        List<object> results = [];
        try
        {
            background.Show(); main.TopMost = true; main.ForceActivate(); Cursor.Position = Point.Empty;
            Call(main, "ReleaseCaptureHotkeys");
            var working = original.Clone(); working.AnimationsEnabled = true; working.TransparencyEnabled = true;
            typeof(MainForm).GetField("preferences", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, working);
            Call(main, "ApplyAppearance"); Call(main, "OpenSettings"); await Task.Delay(500);
            var page = Field<SettingsPage>(main, "settingsPage");
            var toggle = (CheckBox)page.Controls.Find("transparencyToggle", true).Single();
            int D(int value) => (int)Math.Round(value * main.DeviceDpi / 96d);
            foreach (AppearanceMode theme in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                ((RadioButton)page.Controls.Find("theme" + theme, true).Single()).Checked = true;
                await Task.Delay(350);
                foreach (bool enabled in new[] { false, true, false, true })
                {
                    Point[] points = [new(D(6), main.ClientSize.Height / 2), new(main.ClientSize.Width - D(10), main.ClientSize.Height / 2),
                        new(main.ClientSize.Width / 2, D(5)), new(main.ClientSize.Width / 2, main.ClientSize.Height - D(10))];
                    Rectangle[] patches = points.Select(point =>
                    {
                        Point origin = main.PointToScreen(point); LogicalToPhysicalPointForPerMonitorDPI(main.Handle, ref origin);
                        return new Rectangle(origin, new Size(3, 3));
                    }).ToArray();
                    List<(double Ms, int[] Colors)> frames = [];
                    Stopwatch clock = Stopwatch.StartNew();
                    using CancellationTokenSource stop = new();
                    using ManualResetEventSlim ready = new();
                    Task sampler = Task.Run(() =>
                    {
                        Screenshot screenshot = new() { RemoveOutsideScreenArea = false };
                        while (!stop.IsCancellationRequested)
                        {
                            int[] colors = patches.Select(patch =>
                            {
                                using Bitmap image = screenshot.CaptureRectangle(patch);
                                return image.GetPixel(1, 1).ToArgb();
                            }).ToArray();
                            frames.Add((clock.Elapsed.TotalMilliseconds, colors)); ready.Set(); Thread.Sleep(2);
                        }
                    });
                    double input = 0;
                    try
                    {
                        await Task.Run(() => ready.Wait()); await Task.Delay(80);
                        input = clock.Elapsed.TotalMilliseconds; toggle.Checked = enabled;
                        await Task.Delay(500);
                    }
                    finally { stop.Cancel(); await sampler; }
                    if (frames[0].Colors.SequenceEqual(frames[^1].Colors))
                        throw new InvalidOperationException("Desktop capture shows no material change; verify that the Windows desktop is unlocked and visible.");
                    int excursions = 0, maximumError = 0;
                    for (int patch = 0; patch < patches.Length; patch++)
                    {
                        Color first = Color.FromArgb(frames[0].Colors[patch]);
                        Color last = Color.FromArgb(frames[^1].Colors[patch]);
                        foreach (var frame in frames.Where(f => f.Ms >= input))
                        {
                            Color color = Color.FromArgb(frame.Colors[patch]);
                            int Outside(int v, int a, int b) => Math.Max(0, Math.Max(Math.Min(a, b) - v, v - Math.Max(a, b)));
                            int error = Math.Max(Outside(color.R, first.R, last.R), Math.Max(Outside(color.G, first.G, last.G), Outside(color.B, first.B, last.B)));
                            maximumError = Math.Max(maximumError, error);
                            if (error > 16) excursions++;
                        }
                    }
                    var result = new { theme = theme.ToString(), enabled, dpi = main.DeviceDpi, samples = frames.Count, excursions, maximumError };
                    results.Add(result); Console.WriteLine(JsonSerializer.Serialize(result));
                    if (!baseline && excursions > 0)
                        throw new InvalidOperationException($"Transparency switch flashed outside its endpoint colors: {theme}, enabled={enabled}, error={maximumError}.");
                    string directory = Path.GetFullPath($"artifacts/settings-response/{(baseline ? "before" : "after")}-{main.DeviceDpi}");
                    Directory.CreateDirectory(directory);
                    File.WriteAllLines(Path.Combine(directory, $"{theme}-{results.Count}-{enabled}.csv"), new[] { "ms,left,right,top,bottom" }
                        .Concat(frames.Select(f => $"{f.Ms:F3}," + string.Join(",", f.Colors.Select(c => (uint)c)))));
                }
            }
            string output = Path.GetFullPath($"artifacts/settings-response/flicker-{(baseline ? "before" : "after")}-{main.DeviceDpi}.json");
            File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: transparency switch frames were sampled in both themes and directions.");
        }
        finally
        {
            Call(main, "CloseSettings");
            typeof(MainForm).GetField("preferences", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, original);
            Call(main, "ApplyAppearance"); main.TopMost = originalTop; Cursor.Position = originalCursor;
        }
    }
}
