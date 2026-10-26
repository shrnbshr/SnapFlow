using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class ButtonHoverRegression
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Call(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    private sealed class BackingWindow : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { CreateParams value = base.CreateParams; value.ExStyle |= 0x08000000; return value; }
        }
    }
    private static Point Physical(Control control, Point point)
    {
        Point value = control.PointToScreen(point);
        if (!LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref value)) throw new InvalidOperationException("Cannot map physical desktop pixels");
        return value;
    }
    private static int Distance(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    // Offscreen pixel checks: no shown window, pointer input or global shortcut registration.
    internal static void VerifyRendering()
    {
        foreach (CapturePalette palette in new[] { CapturePalette.Light, CapturePalette.Dark })
        foreach (string kind in new[] { "quiet", "surface", "primary", "capture" })
        {
            using Panel page = new() { Size = new Size(160, 70), BackColor = palette.Background };
            using Button button = kind == "capture" ? new CaptureModeButton { Palette = palette, Text = "Capture" }
                : new Button { Text = "Button", Tag = kind == "quiet" ? "quiet" : null };
            button.Bounds = new Rectangle(10, 10, 140, 50);
            page.Controls.Add(button);
            object? state = null;
            UiTween hover;
            if (button is CaptureModeButton) hover = Field<UiTween>(button, "hover");
            else
            {
                palette.StyleButton(button, kind == "primary");
                object states = typeof(CaptureButtonPainter).GetField("states", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                object?[] values = [button, null];
                states.GetType().GetMethod("TryGetValue")!.Invoke(states, values);
                state = values[1]!;
                hover = Field<UiTween>(state, "Hover");
            }
            Color backdrop = Color.RoyalBlue;
            RectangleF face = ButtonElevation.FaceBounds(button.ClientSize, button.DeviceDpi / 96f);
            Point point = new((int)(face.Right - 4 * button.DeviceDpi / 96f), button.Height / 2);
            Color Render(float progress)
            {
                hover.SnapTo(progress);
                using Bitmap normal = new(button.Width, button.Height), mask = new(button.Width, button.Height);
                using (Graphics g = Graphics.FromImage(normal))
                {
                    if (button is CaptureModeButton) button.GetType().GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, [g, false]);
                    else typeof(CaptureButtonPainter).GetMethod("Paint", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [button, state, g, false]);
                }
                using (Graphics g = Graphics.FromImage(mask)) CaptureButtonPainter.PaintBackdrop(button, g);
                uint matte = BackdropSnapshot.CoveredMatte(BackdropSnapshot.RemovedMatte(palette), BackdropSnapshot.Coverage((uint)mask.GetPixel(point.X, point.Y).ToArgb()));
                uint pixel = BackdropSnapshot.RemoveMatte((uint)normal.GetPixel(point.X, point.Y).ToArgb(), matte);
                int transmission = 255 - (int)(pixel >> 24);
                Color result = Color.FromArgb(Math.Min(255, (int)((pixel >> 16) & 255) + (backdrop.R * transmission + 127) / 255),
                    Math.Min(255, (int)((pixel >> 8) & 255) + (backdrop.G * transmission + 127) / 255),
                    Math.Min(255, (int)(pixel & 255) + (backdrop.B * transmission + 127) / 255));
                using Bitmap snapshot = new(page.Width, page.Height);
                using (Graphics g = Graphics.FromImage(snapshot)) { g.Clear(palette.Background); g.DrawImageUnscaled(normal, button.Location); }
                BackdropSnapshot coverage = BackdropSnapshot.Capture(page, snapshot, palette);
                Check(coverage.Pixels[(point.Y + button.Top) * page.Width + point.X + button.Left] == matte,
                    kind + $": page snapshot and live drawing agree at hover={progress:F3}");
                return result;
            }
            Color start = Render(0), end = Render(1);
            int maximum = 0;
            foreach (float progress in new[] { .001f, .01f, .04f, .12f, .25f, .5f, .75f, .92f, .99f })
            {
                Color actual = Render(progress);
                Color expected = Color.FromArgb((int)Math.Round(start.R + (end.R - start.R) * progress),
                    (int)Math.Round(start.G + (end.G - start.G) * progress), (int)Math.Round(start.B + (end.B - start.B) * progress));
                maximum = Math.Max(maximum, Distance(actual, expected));
            }
            Check(maximum <= 2, kind + $": composed hover follows its endpoint colors without a transparency jump (max error {maximum})");
            Check(!button.IsHandleCreated && !page.IsHandleCreated, "offscreen checks never create a desktop window");
        }
    }

    internal static async Task RunAsync(MainForm main, bool baseline, bool allButtonTypes = false)
    {
        string output = Path.GetFullPath($"artifacts/button-hover/{(baseline ? "before" : "after")}/{main.DeviceDpi}");
        Directory.CreateDirectory(output);
        ApplicationPreferences preferences = Field<ApplicationPreferences>(main, "preferences");
        AppearanceMode oldMode = preferences.Appearance;
        bool oldTransparency = preferences.TransparencyEnabled, oldMotion = preferences.AnimationsEnabled, oldTopMost = main.TopMost;
        Rectangle oldBounds = main.Bounds;
        List<string> failures = new();
        using BackingWindow backing = new() { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, TopMost = true, BackColor = Color.RoyalBlue };
        try
        {
            int D(int x) => (int)Math.Round(x * main.DeviceDpi / 96d);
            Rectangle work = Screen.FromControl(main).WorkingArea;
            main.Size = new Size(Math.Min(D(960), work.Width - D(24)), Math.Min(D(760), work.Height - D(24)));
            main.Location = new Point(work.Left + (work.Width - main.Width) / 2, work.Top + (work.Height - main.Height) / 2);
            main.TopMost = true;
            backing.Bounds = work;
            backing.Show();
            SetWindowPos(backing.Handle, main.Handle, 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x200);
            main.ForceActivate();
            if (NativeMethods.GetForegroundWindow() != main.Handle)
                InputHelpers.SendMouseClick(main.PointToScreen(new Point(D(12), D(12))));
            preferences.AnimationsEnabled = true;
            foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            foreach (bool transparent in new[] { true, false })
            {
                Call(main, "CloseSettings"); Call(main, "EndPageTransition");
                preferences.Appearance = mode;
                preferences.TransparencyEnabled = transparent;
                Call(main, "ApplyAppearance");
                InputHelpers.SendMouseMove(main.PointToScreen(new Point(D(12), D(12))));
                main.ForceActivate();
                await Task.Delay(500);
                Check(NativeMethods.GetForegroundWindow() == main.Handle, "hover sampling owns the foreground");
                foreach (string name in new[] { "settingsButton", "regionButton", "fullButton", "scrollButton" })
                    await Sample((Button)main.Controls.Find(name, true).Single(), $"{mode}-{transparent}-{name}");
                Call(main, "OpenSettings");
                await Task.Delay(350);
                foreach (string name in new[] { "cancelSettingsButton", "resetSettingsButton", "saveSettingsButton" })
                    await Sample((Button)main.Controls.Find(name, true).Single(), $"{mode}-{transparent}-{name}");
                if (allButtonTypes)
                {
                    SettingsPage page = (SettingsPage)main.Controls.Find("SettingsPage", true).Single();
                    Panel viewport = Field<Panel>(page, "scroller");
                    ShortcutRecorder recorder = (ShortcutRecorder)page.Controls.Find("regionShortcut", true).Single();
                    if (recorder.Shortcut == Keys.None) recorder.SetShortcut(Keys.F24);
                    foreach (string name in new[] { "themeSystem", "regionShortcut", "regionClear", "scrollingOptionsButton", "aboutRepository" })
                    {
                        ButtonBase button = (ButtonBase)page.Controls.Find(name, true).Single();
                        viewport.ScrollControlIntoView(button);
                        viewport.Invalidate(true);
                        viewport.Update();
                        await Sample(button, $"{mode}-{transparent}-{name}");
                    }
                }
            }
            if (!baseline) Check(failures.Count == 0, "hover feedback has no visible reversals: " + string.Join("; ", failures));
            else Console.WriteLine("BASELINE: " + string.Join("; ", failures));

            async Task Sample(ButtonBase button, string context)
            {
                Point rest = main.PointToScreen(new Point(D(12), D(12)));
                InputHelpers.SendMouseMove(rest);
                await Task.Delay(350);
                Check(button.Visible && button.Enabled, context + ": target is available");
                Point hover = button.PointToScreen(new Point(button.Width / 2, button.Height / 2));
                bool compact = button is ThemeOption or ShortcutRecorder or ShortcutClearButton or SettingsDisclosure or AboutLinkButton;
                RectangleF face = compact ? ButtonElevation.CompactFaceBounds(button.ClientSize, button.DeviceDpi / 96f) :
                    ButtonElevation.FaceBounds(button.ClientSize, button.DeviceDpi / 96f);
                Point sample = Physical(button, new Point((int)face.Right - D(5), button.Height / 2));
                Rectangle patch = new(sample, new Size(2, 2));
                int enters = 0, leaves = 0;
                EventHandler entered = (_, _) => enters++, left = (_, _) => leaves++;
                button.MouseEnter += entered; button.MouseLeave += left;
                Stopwatch timer = Stopwatch.StartNew();
                List<(double Time, Color Color)> samples = new();
                using CancellationTokenSource stop = new();
                using ManualResetEventSlim ready = new();
                Task sampler = Task.Run(() =>
                {
                    Screenshot capture = new() { RemoveOutsideScreenArea = false };
                    while (!stop.IsCancellationRequested)
                    {
                        using Bitmap image = capture.CaptureRectangle(patch);
                        samples.Add((timer.Elapsed.TotalMilliseconds, image.GetPixel(0, 0)));
                        ready.Set(); Thread.Sleep(2);
                    }
                });
                try
                {
                    await Task.Run(() => ready.Wait());
                    await Task.Delay(60);
                    InputHelpers.SendMouseMove(hover);
                    await Task.Delay(800);
                }
                finally
                {
                    stop.Cancel(); await sampler;
                    button.MouseEnter -= entered; button.MouseLeave -= left;
                }
                File.WriteAllLines(Path.Combine(output, context + ".csv"), new[] { "ms,r,g,b" }
                    .Concat(samples.Select(value => $"{value.Time:F2},{value.Color.R},{value.Color.G},{value.Color.B}")));
                using Bitmap strip = new(Math.Max(1, samples.Count) * 5, 32);
                using (Graphics graphics = Graphics.FromImage(strip))
                    for (int i = 0; i < samples.Count; i++)
                    { using SolidBrush brush = new(samples[i].Color); graphics.FillRectangle(brush, i * 5, 0, 5, 32); }
                strip.Save(Path.Combine(output, context + ".png"), ImageFormat.Png);
                Color start = samples[0].Color, end = samples[^1].Color;
                int reversal = 0, excursions = 0, step = 0;
                double previous = 0;
                double denominator = Math.Pow(end.R - start.R, 2) + Math.Pow(end.G - start.G, 2) + Math.Pow(end.B - start.B, 2);
                for (int i = 1; i < samples.Count; i++)
                {
                    Color color = samples[i].Color;
                    step = Math.Max(step, Distance(color, samples[i - 1].Color));
                    double position = denominator < 9 ? 1 : ((color.R - start.R) * (end.R - start.R)
                        + (color.G - start.G) * (end.G - start.G) + (color.B - start.B) * (end.B - start.B)) / denominator;
                    if (position < previous - .15) reversal++;
                    if (position < -.15 || position > 1.15) excursions++;
                    previous = position;
                }
                int settledChange = samples.Where(value => value.Time > 450).Max(value => Distance(value.Color, end));
                Console.WriteLine($"METRIC: {context}: n={samples.Count}, enters={enters}, leaves={leaves}, reversals={reversal}, excursions={excursions}, step={step}, settled={settledChange}, start={start}, end={end}");
                Console.WriteLine($"POINTER: {context}: requested={hover}; actual={Cursor.Position}; targetCenter={button.PointToScreen(new Point(button.Width / 2, button.Height / 2))}; bounds={button.Bounds}");
                if (enters != 1 || leaves != 0 || reversal != 0 || excursions != 0 || settledChange > 3) failures.Add(context);
                if (allButtonTypes)
                {
                    Point first = Physical(button, Point.Empty), last = Physical(button, new Point(button.Width, button.Height));
                    using Bitmap image = new Screenshot { RemoveOutsideScreenArea = false }.CaptureRectangle(Rectangle.FromLTRB(first.X, first.Y, last.X, last.Y));
                    image.Save(Path.Combine(output, context + "-button.png"), ImageFormat.Png);
                }
            }
        }
        finally
        {
            Call(main, "CloseSettings"); Call(main, "EndPageTransition");
            preferences.Appearance = oldMode; preferences.TransparencyEnabled = oldTransparency; preferences.AnimationsEnabled = oldMotion;
            Call(main, "ApplyAppearance"); main.Bounds = oldBounds; main.TopMost = oldTopMost;
        }
    }
}
