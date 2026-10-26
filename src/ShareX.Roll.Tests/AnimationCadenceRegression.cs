using ShareX.Roll;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

internal static class AnimationCadenceRegression
{
    private sealed class Surface : Control
    {
        internal UiTween[] Tweens = [];
        internal readonly List<double> Paints = [];
        internal readonly Stopwatch Clock = new();
        internal Surface()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Color.White);
            for (int i = 0; i < Tweens.Length; i++)
            {
                using SolidBrush brush = new(Color.FromArgb(30, 80 + (int)(150 * Tweens[i].Value), 160));
                e.Graphics.FillRectangle(brush, i % 8 * 48, i / 8 * 36, 10 + 30 * Tweens[i].Value, 24);
            }
            if (Clock.IsRunning) Paints.Add(Clock.Elapsed.TotalMilliseconds);
        }
    }

    private static double Percentile(double[] samples, double fraction) => samples.Length == 0 ? 0 :
        samples[(int)((samples.Length - 1) * fraction)];

    internal static async Task RunAsync(MainForm main)
    {
        bool original = UiMotion.UserEnabled;
        using Form host = new() { ClientSize = new Size(400, 130), Text = "Animation cadence regression" };
        using Surface surface = new() { Dock = DockStyle.Fill };
        host.Controls.Add(surface);
        var results = new List<object>();
        try
        {
            UiMotion.UserEnabled = true;
            host.Show(main);
            await Task.Delay(150);
            if (!UiMotion.Enabled) throw new InvalidOperationException("Windows animations are disabled; live cadence cannot be measured.");
            // ABBA order keeps warmup and changing desktop load from favoring either driver.
            foreach (bool shared in new[] { false, true, true, false })
            {
                surface.Tweens = Enumerable.Range(0, 24).Select(_ =>
                    new UiTween(surface, surface.Invalidate, useFrameClock: shared)).ToArray();
                List<double> updates = [];
                using UiTween probe = new(surface, () => updates.Add(surface.Clock.Elapsed.TotalMilliseconds), useFrameClock: shared);
                surface.Paints.Clear();
                surface.Clock.Restart();
                foreach (UiTween tween in surface.Tweens) tween.AnimateTo(1, 650);
                probe.AnimateTo(1, 650);
                await Task.Delay(850);
                surface.Clock.Stop();
                if (probe.IsRunning || probe.Value != 1 || surface.Tweens.Any(t => t.IsRunning || t.Value != 1))
                    throw new InvalidOperationException("Concurrent animations did not settle and stop.");
                double[] cadence = updates.Zip(updates.Skip(1), (a, b) => b - a).Order().ToArray();
                double[] paintCadence = surface.Paints.Zip(surface.Paints.Skip(1), (a, b) => b - a).Order().ToArray();
                if (cadence.Length < 3 || paintCadence.Length < 3)
                    throw new InvalidOperationException("Concurrent animation did not produce intermediate updates and paints.");
                string driver = shared ? "shared-frame-clock" : "legacy-15ms-timers";
                var result = new
                {
                    driver, dpi = surface.DeviceDpi, simultaneousTweens = 25,
                    updateCount = updates.Count, paintCount = surface.Paints.Count,
                    updateMedianMs = Percentile(cadence, .5), updateP95Ms = Percentile(cadence, .95),
                    paintMedianMs = Percentile(paintCadence, .5), paintP95Ms = Percentile(paintCadence, .95)
                };
                results.Add(result);
                Console.WriteLine(JsonSerializer.Serialize(result));
                foreach (UiTween tween in surface.Tweens) tween.Dispose();
                surface.Tweens = [];
                await Task.Delay(80);
            }
            string output = Path.GetFullPath($"artifacts/animation-audit/cadence-{main.DeviceDpi}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                runtime = Environment.Version.ToString(), os = Environment.OSVersion.ToString(), results,
                measurement = "UI update and WM_PAINT intervals; this is not display presentation or GPU frame timing."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: 25 concurrent animations produce intermediate painted frames and stop in both drivers.");
        }
        finally
        {
            foreach (UiTween tween in surface.Tweens) tween.Dispose();
            surface.Tweens = [];
            host.Close();
            UiMotion.UserEnabled = original;
        }
    }
}
