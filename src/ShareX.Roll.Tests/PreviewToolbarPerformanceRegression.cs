using ShareX.Roll;
using System.Diagnostics;
using System.Reflection;

// UI-thread paint timings only: these samples do not measure desktop presentation FPS.
internal static class PreviewToolbarPerformanceRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private sealed class PaintCounter : NativeWindow, IDisposable
    {
        internal int Paints;
        internal long Ticks;
        internal PaintCounter(Control control) => AssignHandle(control.Handle);
        protected override void WndProc(ref Message message)
        {
            if (message.Msg != 0x000F) { base.WndProc(ref message); return; }
            long start = Stopwatch.GetTimestamp();
            base.WndProc(ref message);
            Ticks += Stopwatch.GetTimestamp() - start;
            Paints++;
        }
        public void Dispose() => ReleaseHandle();
    }

    private static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

    private static void Report(string phase, string operation, List<double> elapsed, long allocated)
    {
        double[] sorted = elapsed.Order().ToArray();
        Console.WriteLine($"PREVIEW-TOOLBAR {phase}/{operation}: samples={sorted.Length}, " +
            $"meanMs={sorted.Average():F3}, p95Ms={sorted[(int)Math.Ceiling(sorted.Length * .95) - 1]:F3}, " +
            $"maxMs={sorted[^1]:F3}, managedBytesPerSample={allocated / sorted.Length}");
    }

    internal static async Task RunAsync(MainForm main)
    {
        bool previousMotion = UiMotion.UserEnabled;
        using Bitmap image = new(1400, 14000);
        using (Graphics graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.White);
            using Font font = new("Microsoft YaHei UI", 20);
            for (int y = 0; y < image.Height; y += 100)
            {
                graphics.FillRectangle(y % 200 == 0 ? Brushes.AliceBlue : Brushes.WhiteSmoke, 0, y, image.Width, 100);
                graphics.DrawString($"SnapFlow 长图预览 {y / 100:000}  ·  toolbar paint measurement", font, Brushes.MidnightBlue, 32, y + 24);
            }
        }
        using Form window = new()
        {
            Text = "SnapFlow preview toolbar paint measurement", ClientSize = new Size(1400, 800),
            StartPosition = FormStartPosition.CenterParent, WindowState = FormWindowState.Maximized
        };
        using PreviewCanvas preview = new()
        {
            Image = image, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill,
            BackColor = Color.White, ZoomTransparencyEnabled = true
        };
        window.Controls.Add(preview);
        try
        {
            UiMotion.UserEnabled = true;
            window.Show(main);
            window.Refresh();
            await Task.Delay(80);
            if (!UiMotion.Enabled)
            {
                Console.WriteLine("SKIP: preview toolbar motion measurement requires enabled Windows animations");
                return;
            }
            preview.SynchronizeImage();
            preview.FitImageWidth(animate: false);
            PreviewZoomBar toolbar = (PreviewZoomBar)preview.Controls.Find("previewZoomBar", true).Single();
            PreviewZoomFade fade = (PreviewZoomFade)preview.Controls.Find("previewZoomFade", true).Single();
            Console.WriteLine($"PREVIEW-TOOLBAR context: dpi={preview.DeviceDpi}, viewport={preview.Width}x{preview.Height}, " +
                $"source={image.Width}x{image.Height}, toolbar={toolbar.Width}x{toolbar.Height}, maximized={window.WindowState == FormWindowState.Maximized}");
            Console.WriteLine("PREVIEW-TOOLBAR timings include UI-thread drawing and native snapshot overhead; not screen FPS or DWM dropped frames.");
            using Bitmap snapshot = new(preview.Width, preview.Height);
            List<PaintCounter> counters = new();
            counters.Add(new PaintCounter(preview));
            counters.Add(new PaintCounter(toolbar));
            foreach (Control button in toolbar.Controls) counters.Add(new PaintCounter(button));
            try
            {
                foreach (string phase in new[] { "hidden", "visible", "fading" })
                {
                    preview.FitImageWidth(animate: false);
                    fade.HideImmediately();
                    if (phase == "visible")
                    {
                        preview.SetZoomBarHovered(true);
                        Stopwatch settle = Stopwatch.StartNew();
                        while (fade.IsAnimating && settle.ElapsedMilliseconds < 1500) await Task.Delay(10);
                        Check(toolbar.Visible && !fade.IsAnimating, "stable toolbar reaches its visible endpoint before sampling");
                    }
                    // Warm caches and JIT before measuring. The fade phase starts afterwards.
                    preview.DrawToBitmap(snapshot, preview.ClientRectangle);
                    preview.Invalidate(); preview.Update();
                    if (phase == "fading") preview.SetZoomBarHovered(true);
                    int paintsBefore = counters.Sum(counter => counter.Paints);
                    long paintTicksBefore = counters.Sum(counter => counter.Ticks);
                    List<double> snapshots = new(), updates = new();
                    long snapshotBytes = 0, updateBytes = 0;
                    int animatedSamples = 0;
                    for (int sample = 0; sample < 16; sample++)
                    {
                        preview.ScrollPreview(44 * preview.DeviceDpi / 96d);
                        if (fade.IsAnimating) animatedSamples++;
                        long allocation = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                        preview.DrawToBitmap(snapshot, preview.ClientRectangle);
                        snapshots.Add(Milliseconds(Stopwatch.GetTimestamp() - start));
                        snapshotBytes += GC.GetAllocatedBytesForCurrentThread() - allocation;
                        allocation = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
                        // OnPaint already synchronously commits the toolbar and each native button.
                        preview.Invalidate(); preview.Update();
                        updates.Add(Milliseconds(Stopwatch.GetTimestamp() - start));
                        updateBytes += GC.GetAllocatedBytesForCurrentThread() - allocation;
                        await Task.Delay(8);
                    }
                    Report(phase, "DrawToBitmap", snapshots, snapshotBytes);
                    Report(phase, "same-frame-Update", updates, updateBytes);
                    Console.WriteLine($"PREVIEW-TOOLBAR {phase}/native-paint: WM_PAINT={counters.Sum(counter => counter.Paints) - paintsBefore}, " +
                        $"inclusiveMs={Milliseconds(counters.Sum(counter => counter.Ticks) - paintTicksBefore):F3}, fadeActiveSamples={animatedSamples}/16; nested control times overlap");
                    if (phase == "fading") Check(animatedSamples > 0, "toolbar fade and long-image viewport motion overlap during measured samples");
                }
            }
            finally { foreach (PaintCounter counter in counters) counter.Dispose(); }
            Check(ReferenceEquals(preview.Image, image), "paint measurement retains the original long image");
        }
        finally
        {
            preview.Image = null;
            window.Close();
            UiMotion.UserEnabled = previousMotion;
        }
    }
}
