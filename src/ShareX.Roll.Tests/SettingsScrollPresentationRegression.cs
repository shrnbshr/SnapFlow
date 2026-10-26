using ShareX.HelpersLib;
using ShareX.Roll;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

// Observe actual desktop pixels, independently of control coordinates and scrollbar Paint events.
internal static class SettingsScrollPresentationRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static T Find<T>(Control parent, string name) where T : Control => (T)parent.Controls.Find(name, true).Single();
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    private record Frame(double Time, int Offset, int[] Pixels);
    private static void MoveMouse(Control control, Point point)
    {
        point = control.PointToScreen(point);
        LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref point);
        nint previous = SetThreadDpiAwarenessContext((nint)(-4));
        try { if (!InputHelpers.SendMouseMove(point)) throw new InvalidOperationException("Physical mouse input is unavailable"); }
        finally { SetThreadDpiAwarenessContext(previous); }
    }

    private static int[] ReadScreen(Rectangle area)
    {
        nint previous = SetThreadDpiAwarenessContext((nint)(-4));
        try
        {
            using Bitmap image = new(area.Width, area.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
            BitmapData data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, image.PixelFormat);
            try
            {
                int[] row = new int[area.Width], result = new int[area.Height * 16];
                for (int y = 0; y < area.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                    for (int x = 0; x < 16; x++) result[y * 16 + x] = row[(x + 1) * area.Width / 17] & 0xFFFFFF;
                }
                return result;
            }
            finally { image.UnlockBits(data); }
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }

    private static (int Shift, double Error) Match(int[] reference, int[] frame, int maximum, int height)
    {
        long best = long.MaxValue;
        int shift = 0;
        for (int dy = 0; dy <= maximum; dy++)
        {
            long error = 0;
            for (int y = 8; y < height - maximum - 8; y += 3)
            for (int x = 0; x < 16; x++)
            {
                int a = reference[(y + dy) * 16 + x], b = frame[y * 16 + x];
                error += Math.Abs((a & 255) - (b & 255)) + Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) + Math.Abs((a >> 16) - (b >> 16));
            }
            if (error < best) { best = error; shift = dy; }
        }
        return (shift, best / (double)(16 * Math.Max(1, (height - maximum - 16) / 3) * 3));
    }

    internal static async Task RunAsync(MainForm main, bool baseline, bool maximized)
    {
        string output = Path.GetFullPath($"artifacts/settings-scroll-presentation/{(baseline ? "before" : "after")}-{main.DeviceDpi}-{(maximized ? "max" : "normal")}");
        Directory.CreateDirectory(output);
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        bool topmost = main.TopMost;
        Point cursor = Cursor.Position;
        bool pressed = false;
        try
        {
            Call(main, "ReleaseCaptureHotkeys");
            ApplicationPreferences working = original.Clone();
            working.TransparencyEnabled = working.AnimationsEnabled = true;
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, working);
            Call(main, "ApplyAppearance");
            main.TopMost = true; main.ForceActivate();
            main.ClientSize = new Size(960 * main.DeviceDpi / 96, 720 * main.DeviceDpi / 96);
            if (maximized) main.WindowState = FormWindowState.Maximized;
            Call(main, "OpenSettings");
            await Task.Delay(400);
            SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
            Panel viewport = Field<Panel>(page, "scroller");
            Control content = Field<Control>(page, "content"), heading = Field<Control>(page, "heading"), footer = Field<Control>(page, "footer");
            Control bar = Find<Control>(page, "settingsScrollBar");
            foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                Find<RadioButton>(page, "theme" + mode).Checked = true;
                Find<Button>(page, "saveSettingsButton").Focus();
                viewport.AutoScrollPosition = Point.Empty;
                await Task.Delay(300);
                Point top = new(content.PointToScreen(new Point(22 * main.DeviceDpi / 96, 0)).X,
                    page.PointToScreen(new Point(0, heading.Bottom + 12)).Y);
                Point bottom = new(content.PointToScreen(new Point(142 * main.DeviceDpi / 96, 0)).X,
                    page.PointToScreen(new Point(0, footer.Top - 12)).Y);
                LogicalToPhysicalPointForPerMonitorDPI(page.Handle, ref top);
                LogicalToPhysicalPointForPerMonitorDPI(page.Handle, ref bottom);
                Rectangle area = Rectangle.FromLTRB(top.X, top.Y, bottom.X, bottom.Y);
                double physicalScale = area.Height / (double)(footer.Top - heading.Bottom - 24);
                int travel = Math.Min(160 * main.DeviceDpi / 96, (int)(area.Height / physicalScale / 3));
                // Absolute mouse coordinates round to physical pixels; allow thumb quantization.
                int maximum = (int)Math.Ceiling((travel + 12) * physicalScale);
                int[] reference = ReadScreen(area);
                foreach (bool wheel in new[] { false, true })
                {
                    viewport.AutoScrollPosition = Point.Empty;
                    await Task.Delay(200);
                    List<Frame> frames = new();
                    int offset = 0;
                    EventHandler moved = (_, _) => Volatile.Write(ref offset, -viewport.AutoScrollPosition.Y);
                    viewport.Controls[0].LocationChanged += moved;
                    Stopwatch clock = Stopwatch.StartNew();
                    using CancellationTokenSource stopSampling = new();
                    Task sampling = Task.Run(() =>
                    {
                        while (!stopSampling.IsCancellationRequested)
                        {
                            int current = Volatile.Read(ref offset);
                            int[] pixels = ReadScreen(area);
                            frames.Add(new Frame(clock.Elapsed.TotalMilliseconds, current, pixels));
                            Thread.Sleep(4);
                        }
                    });
                    await Task.Delay(40);
                    if (wheel)
                        Call(viewport, "OnMouseWheel", new HandledMouseEventArgs(MouseButtons.None, 0, 8, 8, -120));
                    else
                    {
                        // Use physical input so queued native mouse moves and capture agree.
                        RectangleF thumb = (RectangleF)bar.GetType().GetProperty("Thumb", Private)!.GetValue(bar)!;
                        int start = (int)(thumb.Top + thumb.Height / 2), end = start + (int)Math.Round(travel * (bar.Height - thumb.Height) /
                            Math.Max(1, viewport.DisplayRectangle.Height - viewport.ClientSize.Height));
                        MoveMouse(bar, new Point(bar.Width / 2, start));
                        await Task.Delay(40);
                        if (!InputHelpers.SendMouseDown()) throw new InvalidOperationException("Physical mouse down is unavailable");
                        pressed = true;
                        await Task.Delay(20);
                        for (int i = 1; i <= 24; i++)
                        {
                            MoveMouse(bar, new Point(bar.Width / 2, start + (end - start) * i / 24));
                            await Task.Delay(12);
                        }
                        InputHelpers.SendMouseUp(); pressed = false;
                    }
                    await Task.Delay(wheel ? 420 : 220);
                    stopSampling.Cancel();
                    await sampling;
                    viewport.Controls[0].LocationChanged -= moved;
                    string name = $"{mode}-{(wheel ? "wheel" : "drag")}";
                    List<string> rows = ["milliseconds,nativeOffset,screenOffset,meanRgbError"];
                    List<double> changed = new();
                    List<int> lags = new();
                    int previousShift = 0;
                    double finalError = 0, finalShift = 0;
                    foreach (Frame frame in frames)
                    {
                        (int shift, double error) = Match(reference, frame.Pixels, maximum, area.Height);
                        finalError = error; finalShift = shift / physicalScale;
                        rows.Add(FormattableString.Invariant($"{frame.Time:F2},{frame.Offset},{shift / physicalScale:F2},{error:F3}"));
                        if (shift != previousShift) { changed.Add(frame.Time); previousShift = shift; }
                        if (frame.Time > 60 && frame.Time < 390 && error < 12) lags.Add(Math.Abs((int)Math.Round(frame.Offset * physicalScale) - shift));
                    }
                    File.WriteAllLines(Path.Combine(output, name + ".csv"), rows);
                    double[] intervals = changed.Zip(changed.Skip(1), (a, b) => b - a).Order().ToArray();
                    Console.WriteLine($"PRESENTATION: {name}, DPI={main.DeviceDpi}, samples={frames.Count}, visibleMoves={changed.Count}, intervalMedianMs={(intervals.Length == 0 ? 0 : intervals[intervals.Length / 2]):F2}, maxContentLagPhysicalPx={lags.DefaultIfEmpty().Max()}");
                    // Acrylic exposes a stationary desktop while foreground content moves.
                    if (finalError > 12 || Math.Abs(finalShift - offset) > 2 || reference.Distinct().Count() < 32)
                        throw new InvalidOperationException(name + ": screen sampling must identify actual, unobstructed settings content at the final native offset");
                    if (!baseline && (changed.Count < 6 || lags.DefaultIfEmpty().Max() > 70))
                        throw new InvalidOperationException(name + ": screen content must follow the native scroll position without long batched delays");
                }
            }
        }
        finally
        {
            if (pressed) InputHelpers.SendMouseUp();
            Cursor.Position = cursor;
            main.TopMost = topmost;
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, original);
        }
    }
}
