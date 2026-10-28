using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class EditorOpeningFramesRegression
{
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Call(MainForm main, string name, params object[] args) => typeof(MainForm).GetMethod(name, Private)!.Invoke(main, args);
    private static T Field<T>(MainForm main, string name) => (T)typeof(MainForm).GetField(name, Private)!.GetValue(main)!;

    internal static void VerifyInitialization(bool baseline)
    {
        int failures = 0;
        foreach (CapturePalette palette in new[] { CapturePalette.Dark, CapturePalette.Light })
        foreach (bool transparent in new[] { false, true })
        {
            palette.ApplyLibraryTheme();
            using Bitmap source = new(640, 480);
            using RegionCaptureForm editor = ScreenshotEditor.Create(source, palette, new() { TransparencyEnabled = transparent });
            bool created = editor.IsHandleCreated;
            _ = editor.Handle;
            NativeMethods.DwmGetWindowAttribute(editor.Handle, 20, out int dark, sizeof(int));
            NativeMethods.DwmGetWindowAttribute(editor.Handle, 38, out int material, sizeof(int));
            bool expectedDark = palette == CapturePalette.Dark;
            int expectedMaterial = transparent && WindowBackdrop.IsSupported() ? 3 : 1;
            Console.WriteLine($"INITIALIZATION: dark={expectedDark}, transparency={transparent}, already-created={created}, native-dark={dark}, material={material}, expected-material={expectedMaterial}");
            if ((dark != 0) != expectedDark || material != expectedMaterial) failures++;
        }
        CapturePalette.Current.ApplyLibraryTheme();
        if (!baseline && failures != 0) throw new InvalidOperationException($"Editor native appearance is not initialized before Show: {failures}");
    }

    internal static async Task RunAsync(MainForm main, bool baseline, bool pointerOpening = false)
    {
        string output = Path.GetFullPath($"artifacts/editor-opening-frames/{(baseline ? "before" : "after")}-{main.DeviceDpi}{(pointerOpening ? "-pointer" : "")}");
        Directory.CreateDirectory(output);
        var original = Field<ApplicationPreferences>(main, "preferences");
        bool topMost = main.TopMost;
        Point pointer = Cursor.Position;
        FormWindowState originalState = main.WindowState;
        using Form background = new()
        {
            FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
            Bounds = SystemInformation.VirtualScreen, BackColor = Color.FromArgb(36, 48, 72),
            ShowInTaskbar = false
        };
        int whiteFrames = 0;
        try
        {
            background.Show();
            main.TopMost = false;
            main.ForceActivate();
            Cursor.Position = Point.Empty;
            foreach (bool transparent in new[] { false, true })
            for (int repeat = 0; repeat < 2; repeat++)
            {
                if (pointerOpening) main.WindowState = repeat == 0 ? FormWindowState.Normal : FormWindowState.Maximized;
                ApplicationPreferences working = original.Clone();
                working.Appearance = AppearanceMode.Dark;
                working.TransparencyEnabled = transparent;
                typeof(MainForm).GetField("preferences", Private)!.SetValue(main, working);
                Call(main, "ApplyAppearance");
                Bitmap image = new(960, 640);
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.White);
                    graphics.FillRectangle(Brushes.DarkSlateBlue, 440, 280, 80, 80);
                }
                Call(main, "SetPreview", image); // Ownership belongs to MainForm, exactly as after a capture.
                main.ForceActivate();
                await Task.Delay(350);
                if (pointerOpening)
                {
                    Button editButton = (Button)main.Controls.Find("editButton", true).Single();
                    Cursor.Position = editButton.PointToScreen(new Point(editButton.Width / 2, editButton.Height / 2));
                    await Task.Delay(100);
                }
                Rectangle area = Screen.FromControl(main).WorkingArea;
                Point a = main.PointToScreen(Point.Empty), b = main.PointToScreen(new Point(main.ClientSize));
                LogicalToPhysicalPointForPerMonitorDPI(main.Handle, ref a);
                LogicalToPhysicalPointForPerMonitorDPI(main.Handle, ref b);
                // This test's editor is centred in the same working area. Capture both it and its owner.
                float scale = main.DeviceDpi / 96f;
                Size editorSize = new(Math.Min(area.Width, (int)(1000 * scale)), Math.Min(area.Height, (int)(720 * scale)));
                Rectangle editorBounds = new(area.Left + (area.Width - editorSize.Width) / 2,
                    area.Top + (area.Height - editorSize.Height) / 2, editorSize.Width, editorSize.Height);
                Point editorFirst = editorBounds.Location, editorLast = new(editorBounds.Right, editorBounds.Bottom);
                LogicalToPhysicalPointForPerMonitorDPI(main.Handle, ref editorFirst);
                LogicalToPhysicalPointForPerMonitorDPI(main.Handle, ref editorLast);
                editorBounds = Rectangle.FromLTRB(editorFirst.X, editorFirst.Y, editorLast.X, editorLast.Y);
                Rectangle capture = Rectangle.Union(Rectangle.FromLTRB(a.X, a.Y, b.X, b.Y), editorBounds);
                Point mainOrigin = main.Location;
                LogicalToPhysicalPointForPerMonitorDPI(main.Handle, ref mainOrigin);
                Point expectedClient = new(editorBounds.Left + a.X - mainOrigin.X, editorBounds.Top + a.Y - mainOrigin.Y);
                double physicalScale = main.DeviceDpi / 96d * (b.X - a.X) / main.ClientSize.Width;
                List<Point> patches = [];
                foreach (int y in new[] { 10, 38, 68, 88, 110, 138 })
                foreach (double x in new[] { .1, .3, .5, .7, .9 })
                    patches.Add(new(expectedClient.X + (int)((editorBounds.Width - 2 * (a.X - mainOrigin.X)) * x),
                        expectedClient.Y + (int)(y * physicalScale)));
                List<(double Ms, uint[] Colors, Bitmap Image)> colors = [];
                Rectangle strip = Rectangle.FromLTRB(patches.Min(p => p.X), patches.Min(p => p.Y),
                    patches.Max(p => p.X) + 1, patches.Max(p => p.Y) + 1);
                List<(double Ms, Bitmap Image)> frames = [];
                List<string> events = [];
                using CancellationTokenSource stop = new();
                using ManualResetEventSlim ready = new();
                Stopwatch clock = Stopwatch.StartNew();
                Task colorSampler = Task.Run(() =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        double capturedAt = clock.Elapsed.TotalMilliseconds;
                        Bitmap sample = new(strip.Width, strip.Height);
                        using (Graphics g = Graphics.FromImage(sample))
                            g.CopyFromScreen(strip.Location, Point.Empty, strip.Size, CopyPixelOperation.SourceCopy);
                        uint[] values = patches.Select(p => (uint)sample.GetPixel(p.X - strip.Left, p.Y - strip.Top).ToArgb() & 0xFFFFFF).ToArray();
                        colors.Add((capturedAt, values, sample));
                        Thread.Sleep(1);
                    }
                });
                Rectangle client = Rectangle.Empty;
                Point imageSample = Point.Empty;
                double seen = -1;
                RegionCaptureForm? editor = null;
                using System.Windows.Forms.Timer lifecycle = new() { Interval = 10 };
                lifecycle.Tick += (_, _) =>
                {
                    editor ??= Application.OpenForms.OfType<RegionCaptureForm>().FirstOrDefault(f => f.Name == "screenshotEditor");
                    if (editor == null || !editor.Visible || !editor.IsHandleCreated || !IsWindowVisible(editor.Handle)) return;
                    if (NativeMethods.DwmGetWindowAttribute(editor.Handle, 14, out int cloaked, sizeof(int)) >= 0 && cloaked != 0) return;
                    if (seen < 0)
                    {
                        seen = clock.Elapsed.TotalMilliseconds;
                        Point first = editor.PointToScreen(Point.Empty), last = editor.PointToScreen(new Point(editor.ClientSize));
                        LogicalToPhysicalPointForPerMonitorDPI(editor.Handle, ref first);
                        LogicalToPhysicalPointForPerMonitorDPI(editor.Handle, ref last);
                        client = Rectangle.FromLTRB(first.X, first.Y, last.X, last.Y);
                        RectangleF imageBounds = editor.EditorImageBounds;
                        imageSample = editor.PointToScreen(Point.Round(new PointF(imageBounds.Left + imageBounds.Width / 2,
                            imageBounds.Top + imageBounds.Height / 2)));
                        LogicalToPhysicalPointForPerMonitorDPI(editor.Handle, ref imageSample);
                        events.Add($"visible={seen:F3}, foreground={NativeMethods.GetForegroundWindow():X}, editor={editor.Handle:X}, client={client}");
                    }
                    if (clock.Elapsed.TotalMilliseconds - seen > 650) stop.Cancel();
                    if (clock.Elapsed.TotalMilliseconds - seen > 750)
                    {
                        lifecycle.Stop();
                        typeof(RegionCaptureForm).GetMethod("CloseWindow", Private)!.Invoke(editor, [RegionResult.Close]);
                    }
                };
                Task sampler = Task.Run(() =>
                {
                    try
                    {
                        while (!stop.IsCancellationRequested)
                        {
                            using Bitmap full = new(capture.Width, capture.Height);
                            using (Graphics g = Graphics.FromImage(full))
                                g.CopyFromScreen(capture.Location, Point.Empty, capture.Size, CopyPixelOperation.SourceCopy);
                            Bitmap half = new((full.Width + 1) / 2, (full.Height + 1) / 2);
                            using (Graphics g = Graphics.FromImage(half))
                            {
                                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                                g.PixelOffsetMode = PixelOffsetMode.Half;
                                g.DrawImage(full, new Rectangle(Point.Empty, half.Size));
                            }
                            frames.Add((clock.Elapsed.TotalMilliseconds, half));
                            ready.Set();
                            Thread.Sleep(80);
                        }
                    }
                    finally { ready.Set(); }
                });
                try
                {
                    await Task.Run(() => ready.Wait());
                    lifecycle.Start();
                    ((Button)main.Controls.Find("editButton", true).Single()).PerformClick();
                }
                finally { stop.Cancel(); lifecycle.Stop(); await sampler; await colorSampler; }
                try
                {
                    if (seen < 0 || frames.Count < 5) throw new InvalidOperationException("No editor opening frames captured");
                    Bitmap reference = frames[^1].Image;
                    File.WriteAllLines(Path.Combine(output, $"{transparent}-{repeat}-events.log"), events
                        .Concat(new[] { $"capture={capture}, sample={imageSample}" }));
                    Color visibleImage = reference.GetPixel((imageSample.X - capture.Left) / 2, (imageSample.Y - capture.Top) / 2);
                    if (Math.Abs(visibleImage.R - Color.DarkSlateBlue.R) > 3 ||
                        Math.Abs(visibleImage.G - Color.DarkSlateBlue.G) > 3 || Math.Abs(visibleImage.B - Color.DarkSlateBlue.B) > 3)
                    {
                        reference.Save(Path.Combine(output, "invalid-reference.png"));
                        throw new InvalidOperationException($"Desktop sampler did not see the editor's controlled image: {visibleImage}");
                    }
                    Rectangle target = Rectangle.FromLTRB((client.Left - capture.Left) / 2, (client.Top - capture.Top) / 2,
                        (client.Right - capture.Left) / 2, (client.Bottom - capture.Top) / 2);
                    target.Intersect(new Rectangle(Point.Empty, reference.Size));
                    List<string> metrics = ["ms,transient_white,sampled,editor_image_visible"];
                    int maximum = 0;
                    for (int index = 0; index < frames.Count; index++)
                    {
                        var frame = frames[index];
                        Color marker = frame.Image.GetPixel((imageSample.X - capture.Left) / 2, (imageSample.Y - capture.Top) / 2);
                        bool editorImageVisible = Math.Abs(marker.R - Color.DarkSlateBlue.R) <= 3 &&
                            Math.Abs(marker.G - Color.DarkSlateBlue.G) <= 3 && Math.Abs(marker.B - Color.DarkSlateBlue.B) <= 3;
                        int white = 0, sampled = 0;
                        for (int y = target.Top + 4; y < target.Bottom - 4; y += 2)
                        for (int x = target.Left + 4; x < target.Right - 4; x += 2)
                        {
                            Color actual = frame.Image.GetPixel(x, y), settled = reference.GetPixel(x, y);
                            if (actual.R > 245 && actual.G > 245 && actual.B > 245 &&
                                settled.R < 180 && settled.G < 180 && settled.B < 180) white++;
                            sampled++;
                        }
                        maximum = Math.Max(maximum, white);
                        metrics.Add($"{frame.Ms:F3},{white},{sampled},{editorImageVisible}");
                        // Save the entire opening sequence, including its owner, for visual diagnosis.
                        frame.Image.Save(Path.Combine(output, $"{transparent}-{repeat}-{index:D3}.png"), ImageFormat.Png);
                        // A white source image is already visible in the owner's preview.
                        // Do not classify that legitimate pre-editor frame as editor flashing.
                        if (editorImageVisible && frame.Ms >= seen && white > 20) whiteFrames++;
                    }
                    File.WriteAllLines(Path.Combine(output, $"{transparent}-{repeat}.csv"), metrics);
                    File.WriteAllLines(Path.Combine(output, $"{transparent}-{repeat}-events.log"), events);
                    File.WriteAllLines(Path.Combine(output, $"{transparent}-{repeat}-toolbar.csv"),
                        new[] { "ms," + string.Join(",", patches.Select((_, i) => "p" + i)) }
                        .Concat(colors.Select(frame => $"{frame.Ms:F3}," + string.Join(",", frame.Colors.Select(c => c.ToString("X6"))))));
                    uint[] lastColors = colors[^1].Colors;
                    int toolbarWhite = colors.Count(frame => frame.Ms >= seen && frame.Colors.Where((c, i) =>
                        (c & 255) > 245 && ((c >> 8) & 255) > 245 && ((c >> 16) & 255) > 245 &&
                        (lastColors[i] & 255) < 180 && ((lastColors[i] >> 8) & 255) < 180 && ((lastColors[i] >> 16) & 255) < 180).Any());
                    whiteFrames += toolbarWhite;
                    for (int i = 0; i < colors.Count; i++)
                    {
                        var frame = colors[i];
                        if (frame.Colors.Where((c, p) => (c & 255) > 245 && ((c >> 8) & 255) > 245 && ((c >> 16) & 255) > 245 &&
                            (lastColors[p] & 255) < 180 && ((lastColors[p] >> 8) & 255) < 180 && ((lastColors[p] >> 16) & 255) < 180).Any())
                            frame.Image.Save(Path.Combine(output, $"{transparent}-{repeat}-toolbar-{i:D3}.png"), ImageFormat.Png);
                    }
                    Console.WriteLine($"TOOLBAR: transparency={transparent}, repeat={repeat}, samples={colors.Count}, transient white={toolbarWhite}");
                    Console.WriteLine($"FRAMES: transparency={transparent}, repeat={repeat}, count={frames.Count}, visible={seen:F2}, max white including owner={maximum}");
                }
                finally
                {
                    foreach (var frame in frames) frame.Image.Dispose();
                    foreach (var frame in colors) frame.Image.Dispose();
                }
            }
            if (!baseline && whiteFrames != 0) throw new InvalidOperationException($"Editor opening contains transient white: {whiteFrames} sampled frames");
        }
        finally
        {
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, original);
            Call(main, "ApplyAppearance");
            main.TopMost = topMost;
            main.WindowState = originalState;
            Cursor.Position = pointer;
        }
    }
}
