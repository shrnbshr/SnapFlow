using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.Reflection;
using System.IO;

internal static class NavigationContinuityRegression
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
    private static Bitmap Capture(MainForm main) => new Screenshot { RemoveOutsideScreenArea = false }.CaptureWindow(main.Handle);

    private static double Difference(Bitmap a, Bitmap b)
    {
        if (a.Size != b.Size) throw new InvalidOperationException("Navigation changes the window size");
        long changed = 0;
        for (int y = 60; y < a.Height; y += 2)
            for (int x = 2; x < a.Width - 2; x += 2)
            {
                Color first = a.GetPixel(x, y), last = b.GetPixel(x, y);
                if (Math.Max(Math.Abs(first.R - last.R), Math.Max(Math.Abs(first.G - last.G), Math.Abs(first.B - last.B))) > 20) changed++;
            }
        return (double)changed / ((a.Width - 4) / 2 * ((a.Height - 60 + 1) / 2));
    }

    private static double Position(Bitmap frame, Bitmap before, Bitmap after)
    {
        double numerator = 0, denominator = 0;
        for (int y = 0; y < frame.Height; y += 8)
            for (int x = 0; x < frame.Width; x += 8)
            {
                Color start = before.GetPixel(x, y), end = after.GetPixel(x, y), current = frame.GetPixel(x, y);
                double r = end.R - start.R, g = end.G - start.G, b = end.B - start.B;
                numerator += (current.R - start.R) * r + (current.G - start.G) * g + (current.B - start.B) * b;
                denominator += r * r + g * g + b * b;
            }
        if (denominator == 0)
            throw new InvalidOperationException("Desktop capture shows identical pages; verify that the Windows desktop is unlocked and visible.");
        return numerator / denominator;
    }

    private static async Task VerifyLiveFrames(MainForm main, string output, string direction)
    {
        // DWM bounds are physical pixels even when the test emulates a 96 DPI application.
        Rectangle rectangle = CaptureHelpers.GetWindowRectangle(main.Handle);
        List<Bitmap> frames = new();
        using CancellationTokenSource stop = new();
        using ManualResetEventSlim ready = new();
        Task sampler = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                frames.Add(new Screenshot { RemoveOutsideScreenArea = false }.CaptureRectangle(rectangle));
                ready.Set();
                Thread.Sleep(8);
            }
        });
        try
        {
            await Task.Run(() => ready.Wait());
            await Task.Delay(60);
            Call(main, direction);
            await Task.Delay(420);
        }
        finally { stop.Cancel(); await sampler; }
        try
        {
            double previous = 0;
            int intermediate = 0;
            for (int i = 0; i < frames.Count; i++)
            {
                frames[i].Save(Path.Combine(output, direction + "-live-" + i.ToString("D2") + ".png"), ImageFormat.Png);
                double position = Position(frames[i], frames[0], frames[^1]);
                Console.WriteLine($"{direction} frame {i}: {position:F3}");
                if (position < previous - .1) throw new InvalidOperationException("Live navigation flashes back to an earlier page");
                previous = Math.Max(previous, position);
                if (position > .05 && position < .95) intermediate++;
            }
            if (intermediate < 2) throw new InvalidOperationException("Live navigation has no visible intermediate frames");
            Console.WriteLine("PASS: continuous desktop sampling includes page preparation and shows no reverse flash: " + direction);
        }
        finally { foreach (Bitmap frame in frames) frame.Dispose(); }
    }

    internal static async Task RunAsync(MainForm main, bool maximized = false)
    {
        string output = Path.GetFullPath("artifacts/navigation-fix/" + (Environment.GetEnvironmentVariable("SNAPFLOW_TEST_STAGE") ?? "fixed") + "/" + main.DeviceDpi + (maximized ? "-max" : ""));
        Directory.CreateDirectory(output);
        ApplicationPreferences preferences = Field<ApplicationPreferences>(main, "preferences");
        AppearanceMode originalMode = preferences.Appearance;
        bool originalMotion = preferences.AnimationsEnabled;
        FormWindowState originalState = main.WindowState;
        Point originalCursor = Cursor.Position;
        List<string> failures = new();
        Control workspace = Field<Control>(main, "workspace");
        void VerifyCover(object? sender, EventArgs args)
        {
            if (!UiMotion.Enabled || !main.Visible) return;
            PageTransition? cover = Field<PageTransition?>(main, "pageTransition");
            if (cover == null || !cover.Visible || !cover.IsHandleCreated || main.Controls.GetChildIndex(cover) != 0)
                failures.Add("Live page visibility changes before the animation cover is painted");
        }
        workspace.VisibleChanged += VerifyCover;
        try
        {
            main.TopMost = true;
            if (maximized) main.WindowState = FormWindowState.Maximized;
            main.ForceActivate();
            preferences.AnimationsEnabled = true;
            foreach (AppearanceMode mode in new[] { AppearanceMode.Dark, AppearanceMode.Light })
            {
                preferences.Appearance = mode;
                Call(main, "ApplyAppearance");
                if (!UiMotion.Enabled) throw new InvalidOperationException("Windows animation must be enabled for this regression");
                foreach (string direction in new[] { "OpenSettings", "CloseSettings" })
                {
                    Cursor.Position = Point.Empty;
                    main.Update();
                    await Task.Delay(250);
                    using Bitmap before = Capture(main);
                    Call(main, direction);
                    PageTransition transition = Field<PageTransition>(main, "pageTransition");
                    UiTween progress = Field<UiTween>(transition, "progress");
                    progress.SnapTo(0);
                    transition.Refresh();
                    await Task.Delay(40);
                    using Bitmap first = Capture(main);
                    progress.SnapTo(1);
                    transition.Refresh();
                    await Task.Delay(40);
                    using Bitmap last = Capture(main);
                    Call(main, "EndPageTransition");
                    main.Refresh();
                    await Task.Delay(100);
                    using Bitmap after = Capture(main);
                    string prefix = mode + "-" + direction;
                    before.Save(Path.Combine(output, prefix + "-before.png"), ImageFormat.Png);
                    first.Save(Path.Combine(output, prefix + "-first.png"), ImageFormat.Png);
                    last.Save(Path.Combine(output, prefix + "-last.png"), ImageFormat.Png);
                    after.Save(Path.Combine(output, prefix + "-after.png"), ImageFormat.Png);
                    double startDifference = Difference(before, first), endDifference = Difference(last, after);
                    Console.WriteLine($"{prefix}: start={startDifference:P3}, end={endDifference:P3}");
                    if (startDifference > .005 || endDifference > .005) failures.Add(prefix + " changes appearance at an animation endpoint");
                }
            }
            await VerifyLiveFrames(main, output, "OpenSettings");
            await VerifyLiveFrames(main, output, "CloseSettings");
            if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
            Console.WriteLine("PASS: dark and light navigation keeps both animation endpoints visually continuous");
        }
        finally
        {
            workspace.VisibleChanged -= VerifyCover;
            Call(main, "CloseSettings");
            preferences.Appearance = originalMode;
            preferences.AnimationsEnabled = originalMotion;
            Call(main, "ApplyAppearance");
            main.TopMost = false;
            main.WindowState = originalState;
            Cursor.Position = originalCursor;
        }
    }
}
