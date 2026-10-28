using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class StartupPaintingRegression
{
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);

    private sealed class PaintProbe : NativeWindow, IDisposable
    {
        private readonly Action? painting;
        internal int Frames;
        internal PaintProbe(Control control, Action? painting = null)
        {
            this.painting = painting;
            AssignHandle(control.Handle);
        }
        protected override void WndProc(ref Message message)
        {
            bool paint = message.Msg is 0xF or 0x318;
            if (paint) Frames++;
            base.WndProc(ref message);
            if (paint) painting?.Invoke();
        }
        public void Dispose() => ReleaseHandle();
    }

    private static PaintProbe? startup;
    internal static void ObserveStartup(MainForm main)
    {
        main.HandleCreated += (_, _) => startup = new(main);
        main.Disposed += (_, _) => { startup?.Dispose(); startup = null; };
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    internal static async Task RunAsync(MainForm main, bool baseline)
    {
        var preferencesField = typeof(MainForm).GetField("preferences", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var original = (ApplicationPreferences)preferencesField.GetValue(main)!;
        var working = original.Clone();
        preferencesField.SetValue(main, working);
        var apply = typeof(MainForm).GetMethod("ApplyAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!;
        bool wasTopMost = main.TopMost;
        Point originalCursor = Cursor.Position;
        using Form background = new()
        {
            FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
            Bounds = SystemInformation.VirtualScreen, BackColor = Color.RoyalBlue,
            ShowInTaskbar = false, TopMost = true
        };
        List<string> failures = [];
        try
        {
            Console.WriteLine($"STARTUP: complete frames before Shown={startup?.Frames ?? 0}");
            if (startup?.Frames is not > 0) failures.Add("cold startup exposes a pending first frame");
            background.Show();
            main.TopMost = true;
            main.ForceActivate();
            Cursor.Position = Point.Empty;
            foreach (AppearanceMode mode in new[] { AppearanceMode.Dark, AppearanceMode.Light })
            foreach (bool transparent in new[] { true, false })
            {
                main.Hide();
                working.Appearance = mode;
                working.TransparencyEnabled = transparent;
                apply.Invoke(main, null);
                using PaintProbe probe = new(main);
                main.Show();
                Check(NativeMethods.DwmGetWindowAttribute(main.Handle, 14, out int mainCloak, sizeof(int)) >= 0 && mainCloak == 0,
                    "main window releases its temporary DWM cloak after first-frame painting");
                bool painted = probe.Frames > 0;
                Console.WriteLine($"SHOW: main {mode}, transparency={transparent}, complete frames before Show returns={probe.Frames}");
                if (!painted) failures.Add($"main {mode}/{transparent} exposes a pending first frame");
                await Task.Delay(250);
                await VerifyCaptureReturnAsync(main, mode, transparent);

                CapturePalette palette = CapturePalette.For(mode);
                using Bitmap image = new(640, 360);
                using (Graphics graphics = Graphics.FromImage(image)) graphics.Clear(Color.CornflowerBlue);
                using RegionCaptureForm editor = ScreenshotEditor.Create(image, palette, working);
                List<string> unstyled = [];
                List<PaintProbe> stripProbes = [];
                void Observe(Control control)
                {
                    void Attach()
                    {
                        if (control.Name == "editorToolbar" || control.Name == "editorActions")
                        {
                            ToolStrip strip = (ToolStrip)control;
                            stripProbes.Add(new(control, () =>
                            {
                                if (strip.BackColor != palette.Background || strip.Renderer is not EditorToolStripRenderer)
                                    unstyled.Add(control.Name);
                            }));
                        }
                    }
                    control.HandleCreated += (_, _) => Attach();
                    if (control.IsHandleCreated) Attach();
                    control.ControlAdded += (_, e) => { if (e.Control != null) Observe(e.Control); };
                    foreach (Control child in control.Controls) Observe(child);
                }
                try
                {
                    Observe(editor);
                    _ = editor.Handle;
                    using PaintProbe editorProbe = new(editor);
                    editor.Show(main);
                    Check(NativeMethods.DwmGetWindowAttribute(editor.Handle, 14, out int editorCloak, sizeof(int)) >= 0 && editorCloak == 0,
                        "editor releases its temporary DWM cloak after first-frame painting");
                    Console.WriteLine($"SHOW: editor {mode}, transparency={transparent}, frames={editorProbe.Frames}");
                    if (editorProbe.Frames == 0) failures.Add($"editor {mode}/{transparent} exposes a pending first frame");
                    if (editor.Controls.Find("editorToolbar", true).Length != 1 || !editor.IsEditorCropPending)
                        failures.Add($"editor {mode}/{transparent} first frame lacks the toolbar or initial crop");
                    await Task.Delay(300);
                    if (unstyled.Count != 0) failures.Add($"editor {mode}/{transparent} paints unstyled controls: {string.Join(",", unstyled.Distinct())}");
                    Check(stripProbes.Count == 2 && stripProbes.All(p => p.Frames > 0), "both toolbar rows were observed painting");
                    Check(editor.Opacity == 1 && editor.TransparencyKey.IsEmpty, "show preserves native opacity and hit testing");
                    using Bitmap result = editor.GetResultImage();
                    Check(result.Size == image.Size && result.GetPixel(100, 100).ToArgb() == image.GetPixel(100, 100).ToArgb(),
                        "first-frame preparation preserves screenshot pixels");
                    editor.Close();
                }
                finally { foreach (PaintProbe stripProbe in stripProbes) stripProbe.Dispose(); }
            }
            foreach (string failure in failures) Console.WriteLine("PENDING: " + failure);
            if (!baseline) Check(failures.Count == 0, "main, capture return and editor show complete themed frames synchronously");
        }
        finally
        {
            preferencesField.SetValue(main, original);
            apply.Invoke(main, null);
            main.TopMost = wasTopMost;
            Cursor.Position = originalCursor;
            main.Show();
        }
    }

    private static async Task VerifyCaptureReturnAsync(MainForm main, AppearanceMode mode, bool transparent)
    {
        var preview = (PictureBox)typeof(MainForm).GetField("preview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        var busy = typeof(MainForm).GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int repeat = 0; repeat < 2; repeat++)
        {
            Image? previous = preview.Image;
            Point[] points = [main.PointToScreen(new Point(6, main.ClientSize.Height / 2)),
                main.PointToScreen(new Point(main.ClientSize.Width - 6, main.ClientSize.Height / 2)),
                preview.PointToScreen(new Point(preview.Width / 2, preview.Height / 2))];
            for (int i = 0; i < points.Length; i++)
                Check(LogicalToPhysicalPointForPerMonitorDPI(main.Handle, ref points[i]), "capture sample maps to the visible desktop");
            List<(double Ms, int[] Colors)> frames = [];
            string captureOutput = Path.GetFullPath("artifacts/startup-painting");
            Directory.CreateDirectory(captureOutput);
            Rectangle sampleBounds = Rectangle.FromLTRB(points.Min(p => p.X), points.Min(p => p.Y),
                points.Max(p => p.X) + 1, points.Max(p => p.Y) + 1);
            Stopwatch watch = Stopwatch.StartNew();
            using CancellationTokenSource stop = new();
            using ManualResetEventSlim ready = new();
            Task sampler = Task.Run(() =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        using Bitmap sample = new(sampleBounds.Width, sampleBounds.Height);
                        using (Graphics graphics = Graphics.FromImage(sample))
                            graphics.CopyFromScreen(sampleBounds.Location, Point.Empty, sampleBounds.Size, CopyPixelOperation.SourceCopy);
                        int[] colors = points.Select(point => sample.GetPixel(point.X - sampleBounds.X, point.Y - sampleBounds.Y).ToArgb()).ToArray();
                        frames.Add((watch.Elapsed.TotalMilliseconds, colors));
                        if (colors.Any(c => (uint)c == 0xFFFFFFFF))
                        {
                            sample.Save(Path.Combine(captureOutput, $"stripe-{main.DeviceDpi}-{mode}-{transparent}-{repeat}.png"));
                        }
                        ready.Set();
                        Thread.Sleep(2);
                    }
                }
                finally { ready.Set(); }
            });
            using PaintProbe probe = new(main);
            try
            {
                await Task.Run(() => ready.Wait());
                // Exercise the actual capture button; never open the editor in this path.
                ((Button)main.Controls.Find("fullButton", true).Single()).PerformClick();
                Check(!main.Visible && (bool)busy.GetValue(main)!, "capture button hides the main window while capturing");
                while ((bool)busy.GetValue(main)! && watch.Elapsed.TotalSeconds < 10) await Task.Delay(10);
                Check(main.Visible && !(bool)busy.GetValue(main)! && probe.Frames > 0,
                    "capture returns directly to a painted main window without clicking Edit");
                await Task.Delay(300);
            }
            finally { stop.Cancel(); await sampler; }
            Check(!ReferenceEquals(previous, preview.Image) && preview.Image != null,
                "first and repeated captures replace the main preview");
            Check(!Application.OpenForms.OfType<RegionCaptureForm>().Any(), "capture return does not open an editor");
            Check(((Bitmap)preview.Image!).GetPixel(preview.Image.Width / 2, preview.Image.Height / 2).ToArgb() == Color.RoyalBlue.ToArgb(),
                "capture uses the controlled test background");
            int white = frames.Count(frame => frame.Colors.Any(argb =>
            {
                Color color = Color.FromArgb(argb);
                return color.R > 250 && color.G > 250 && color.B > 250;
            }));
            string output = Path.GetFullPath("artifacts/startup-painting");
            Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output, $"capture-{main.DeviceDpi}-{mode}-{transparent}-{repeat}.csv"),
                new[] { "ms,left,right,preview" }.Concat(frames.Select(frame => $"{frame.Ms:F3}," + string.Join(",", frame.Colors.Select(c => (uint)c)))));
            Console.WriteLine($"CAPTURE RETURN: dpi={main.DeviceDpi}, theme={mode}, transparency={transparent}, repeat={repeat}, samples={frames.Count}, white={white}");
            Check(white == 0, "main-window return contains no white samples");
        }
    }
}
