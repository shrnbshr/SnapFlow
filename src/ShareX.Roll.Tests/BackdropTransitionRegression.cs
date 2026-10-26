using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class BackdropTransitionRegression
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Call(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    private static Point Physical(Control control, Point point)
    {
        point = control.PointToScreen(point);
        if (!LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref point)) throw new InvalidOperationException("Cannot map desktop pixels");
        return point;
    }

    private sealed class BackingWindow : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { CreateParams value = base.CreateParams; value.ExStyle |= 0x08000000; return value; }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.RoyalBlue);
            e.Graphics.FillRectangle(Brushes.OrangeRed, Width / 2, 0, Width, Height);
        }
    }

    private sealed class Messages : NativeWindow, IDisposable
    {
        private readonly string name;
        private readonly Stopwatch clock;
        private readonly ConcurrentQueue<string> messages;
        internal Messages(Control control, Stopwatch clock, ConcurrentQueue<string> messages)
        {
            name = control.Name;
            this.clock = clock;
            this.messages = messages;
            AssignHandle(control.Handle);
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg is 0x14 or 0xF or 0x18 or 0x47 or 0x82)
                messages.Enqueue($"{clock.Elapsed.TotalMilliseconds:F2},{name},WM_{message.Msg:X}");
            base.WndProc(ref message);
        }
        public void Dispose() => ReleaseHandle();
    }

    private static Color Average(Bitmap bitmap)
    {
        long r = 0, g = 0, b = 0;
        for (int y = 0; y < bitmap.Height; y += 2)
        for (int x = 0; x < bitmap.Width; x += 2)
        {
            Color color = bitmap.GetPixel(x, y); r += color.R; g += color.G; b += color.B;
        }
        int count = ((bitmap.Width + 1) / 2) * ((bitmap.Height + 1) / 2);
        return Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
    }
    private static int Distance(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    internal static async Task RunAsync(MainForm main, bool baseline)
    {
        string output = Path.GetFullPath($"artifacts/acrylic-transition/{(baseline ? "before" : "after")}/{main.DeviceDpi}");
        Directory.CreateDirectory(output);
        ApplicationPreferences preferences = Field<ApplicationPreferences>(main, "preferences");
        AppearanceMode originalMode = preferences.Appearance;
        bool originalMotion = preferences.AnimationsEnabled;
        bool originalTransparency = preferences.TransparencyEnabled;
        Rectangle originalBounds = main.Bounds;
        bool originalTopMost = main.TopMost;
        List<string> failures = new();
        using BackingWindow background = new()
        {
            ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual, Bounds = Screen.FromControl(main).WorkingArea, TopMost = true
        };
        try
        {
            Call(main, "CloseSettings");
            Call(main, "EndPageTransition");
            preferences.AnimationsEnabled = true;
            preferences.TransparencyEnabled = true;
            int D(int value) => (int)Math.Round(value * main.DeviceDpi / 96d);
            Rectangle work = Screen.FromControl(main).WorkingArea;
            main.Size = new Size(Math.Min(D(920), work.Width - D(24)), Math.Min(D(720), work.Height - D(24)));
            main.Location = new Point(work.Left + (work.Width - main.Width) / 2, work.Top + (work.Height - main.Height) / 2);
            background.Show();
            main.TopMost = true;
            main.ForceActivate();
            SetWindowPos(background.Handle, main.Handle, 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x200);
            foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                preferences.Appearance = mode;
                Call(main, "ApplyAppearance");
                if (!UiMotion.Enabled || !Field<WindowBackdrop>(main, "windowBackdrop").IsActive)
                    throw new InvalidOperationException("This regression needs real Acrylic and enabled Windows animations");
                await Task.Delay(350);
                for (int repeat = 0; repeat < 3; repeat++)
                foreach (string direction in new[] { "OpenSettings", "CloseSettings" })
                {
                    main.ForceActivate();
                    if (NativeMethods.GetForegroundWindow() != main.Handle)
                        InputHelpers.SendMouseClick(main.PointToScreen(new Point(D(12), D(12))));
                    await Task.Delay(450); // Settle DWM's separate inactive-to-active material animation.
                    if (NativeMethods.GetForegroundWindow() != main.Handle)
                        throw new InvalidOperationException("The edge regression must own the foreground before sampling");
                    main.Refresh();
                    await Task.Delay(200);
                    Point[] logical = [new(D(5), main.ClientSize.Height / 2), new(main.ClientSize.Width - D(9), main.ClientSize.Height / 2),
                        new(main.ClientSize.Width / 2, D(4)), new(main.ClientSize.Width / 2, main.ClientSize.Height - D(8))];
                    Rectangle[] patches = logical.Select(point => new Rectangle(Physical(main, point),
                        new Size(Physical(main, new Point(point.X + D(4), point.Y + D(4))).X - Physical(main, point).X,
                            Physical(main, new Point(point.X + D(4), point.Y + D(4))).Y - Physical(main, point).Y))).ToArray();
                    Stopwatch clock = Stopwatch.StartNew();
                    ConcurrentQueue<string> messages = new();
                    List<Messages> observers = new() { new(main, clock, messages) };
                    void Added(object? sender, ControlEventArgs args)
                    {
                        if (args.Control is { IsHandleCreated: true } control) observers.Add(new(control, clock, messages));
                    }
                    main.ControlAdded += Added;
                    List<(double Time, Color[] Edges)> samples = new();
                    using CancellationTokenSource stop = new();
                    using ManualResetEventSlim ready = new();
                    Task sampler = Task.Run(() =>
                    {
                        Screenshot screenshot = new() { RemoveOutsideScreenArea = false };
                        while (!stop.IsCancellationRequested)
                        {
                            Color[] colors = patches.Select(rectangle => { using Bitmap bitmap = screenshot.CaptureRectangle(rectangle); return Average(bitmap); }).ToArray();
                            samples.Add((clock.Elapsed.TotalMilliseconds, colors));
                            ready.Set();
                            Thread.Sleep(1);
                        }
                    });
                    try
                    {
                        await Task.Run(() => ready.Wait());
                        await Task.Delay(80);
                        if (repeat == 2)
                        {
                            string buttonName = direction == "OpenSettings" ? "settingsButton" : "cancelSettingsButton";
                            Button button = (Button)main.Controls.Find(buttonName, true).Single();
                            if (!InputHelpers.SendMouseClick(button.PointToScreen(new Point(button.Width / 2, button.Height / 2))))
                                throw new InvalidOperationException("Cannot send the physical navigation click");
                        }
                        else Call(main, direction);
                        bool ExpectedPage() => (Field<SettingsPage?>(main, "settingsPage") != null) == (direction == "OpenSettings");
                        Stopwatch settling = Stopwatch.StartNew();
                        while (!ExpectedPage() || Field<PageTransition?>(main, "pageTransition") != null)
                        {
                            if (settling.Elapsed.TotalSeconds > 4)
                                throw new InvalidOperationException("The navigation input did not finish on the expected page");
                            await Task.Delay(15);
                        }
                        await Task.Delay(150); // Include cover destruction and the first complete live repaint.
                    }
                    finally
                    {
                        stop.Cancel(); await sampler;
                        main.ControlAdded -= Added;
                        foreach (Messages observer in observers) observer.Dispose();
                    }
                    string prefix = $"{mode}-{repeat}-{direction}";
                    File.WriteAllLines(Path.Combine(output, prefix + "-messages.csv"), messages);
                    File.WriteAllLines(Path.Combine(output, prefix + "-edges.csv"), new[] { "ms,left,right,top,bottom" }
                        .Concat(samples.Select(sample => $"{sample.Time:F2}," + string.Join(",", sample.Edges.Select(color => $"{color.R}:{color.G}:{color.B}")))));
                    using Bitmap strip = new(Math.Max(1, samples.Count) * 8, 4 * 24);
                    using (Graphics graphics = Graphics.FromImage(strip))
                        for (int i = 0; i < samples.Count; i++)
                            for (int edge = 0; edge < 4; edge++)
                            {
                                using SolidBrush brush = new(samples[i].Edges[edge]);
                                graphics.FillRectangle(brush, i * 8, edge * 24, 8, 24);
                            }
                    strip.Save(Path.Combine(output, prefix + "-edges.png"), ImageFormat.Png);
                    int maximum = samples.Max(sample => Enumerable.Range(0, 4).Max(edge => Distance(sample.Edges[edge], samples[0].Edges[edge])));
                    Console.WriteLine($"{prefix}: samples={samples.Count}, maximum unchanged-edge color change={maximum}");
                    if (maximum > 4) failures.Add(prefix + $": transparent edge flashed by {maximum} RGB levels");
                }
            }
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("; ", failures));
            Console.WriteLine("PASS: both navigation directions keep all four Acrylic edges stable in light and dark themes");
        }
        finally
        {
            Call(main, "CloseSettings");
            Call(main, "EndPageTransition");
            preferences.Appearance = originalMode;
            preferences.AnimationsEnabled = originalMotion;
            preferences.TransparencyEnabled = originalTransparency;
            Call(main, "ApplyAppearance");
            main.Bounds = originalBounds;
            main.TopMost = originalTopMost;
        }
    }
}
