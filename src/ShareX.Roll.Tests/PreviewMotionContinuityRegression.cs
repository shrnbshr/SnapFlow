using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Diagnostics;
using System.IO;
using System.Drawing.Imaging;
using System.Reflection;

internal static class PreviewMotionContinuityRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private static void Call(object target, string name, params object[] args) => target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);

    internal static async Task RunAsync(MainForm main)
    {
        await VerifyCaptureFadeAsync();
        bool originalMotion = UiMotion.UserEnabled;
        using Bitmap source = new(1600, 1200);
        using (Graphics graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.White);
            for (int y = 0; y < source.Height; y += 200)
            for (int x = 0; x < source.Width; x += 200)
                graphics.FillRectangle((x / 200 + y / 200) % 2 == 0 ? Brushes.CornflowerBlue : Brushes.Coral,
                    x, y, 200, 200);
        }
        // Native controls remain visible to their clocks without pointer movement affecting the test.
        using PreviewCanvas preview = new()
        {
            Location = new Point(-10000, -10000), Size = new Size(480, 360),
            Image = source, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White
        };
        main.Controls.Add(preview);
        try
        {
            UiMotion.UserEnabled = false;
            preview.SynchronizeImage();
            using Bitmap frame = new(preview.Width, preview.Height);
            preview.DrawToBitmap(frame, preview.ClientRectangle);
            preview.ZoomBy(4, new PointF(preview.Width / 2f, preview.Height / 2f));
            double enlarged = preview.ZoomFactor;
            UiMotion.UserEnabled = true;
            if (!UiMotion.Enabled) { Console.WriteLine("SKIP: Windows has disabled animated preview transitions"); return; }

            preview.FitImage();
            await Task.Delay(45);
            double fit = Math.Min((double)preview.Width / source.Width, (double)preview.Height / source.Height);
            Check(preview.IsViewportAnimating && preview.ZoomFactor > fit && preview.ZoomFactor < enlarged,
                "fit has an intermediate presentation before a pointer grab");
            RectangleF displayed = preview.ImageBounds;
            preview.DrawToBitmap(frame, preview.ClientRectangle);
            Call(preview, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, preview.Width / 2, preview.Height / 2, 0));
            try
            {
                using Bitmap grabbed = new(preview.Width, preview.Height);
                preview.DrawToBitmap(grabbed, preview.ClientRectangle);
                string output = Path.GetFullPath("artifacts/animation-audit");
                Directory.CreateDirectory(output);
                frame.Save(Path.Combine(output, $"fit-before-grab-{preview.DeviceDpi}.png"), ImageFormat.Png);
                grabbed.Save(Path.Combine(output, $"fit-after-grab-{preview.DeviceDpi}.png"), ImageFormat.Png);
                Check(!preview.IsViewportAnimating && preview.ImageBounds == displayed,
                    "grabbing an in-flight fit stops at the displayed image transform");
                int changed = 0, total = 0;
                for (int y = 20; y < preview.Height - 20; y += 5)
                for (int x = 20; x < preview.Width - 20; x += 5)
                {
                    Color before = frame.GetPixel(x, y), after = grabbed.GetPixel(x, y);
                    if (Math.Max(Math.Abs(before.R - after.R), Math.Max(Math.Abs(before.G - after.G), Math.Abs(before.B - after.B))) > 8)
                        changed++;
                    total++;
                }
                Check((double)changed / total < .005,
                    $"grabbing a fit preserves rendered pixels instead of jumping to its destination ({changed}/{total} differ)");
            }
            finally { Call(preview, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, preview.Width / 2, preview.Height / 2, 0)); }
            preview.FitImage();
            Stopwatch settle = Stopwatch.StartNew();
            while (preview.IsViewportAnimating && settle.ElapsedMilliseconds < 2000) await Task.Delay(20);
            Check(!preview.IsViewportAnimating && preview.ZoomFactor == fit,
                "resuming fit reaches its exact destination and releases the frame clock");
        }
        finally
        {
            preview.Image = null;
            main.Controls.Remove(preview);
            UiMotion.UserEnabled = originalMotion;
        }
    }

    private static async Task VerifyCaptureFadeAsync()
    {
        Type type = typeof(RegionCaptureForm).Assembly.GetType("ShareX.ScreenCaptureLib.OpacityAnimation", true)!;
        object fade = Activator.CreateInstance(type, nonPublic: true)!;
        T Read<T>(string name) => (T)type.GetProperty(name)!.GetValue(fade)!;
        void SetDuration(string name, TimeSpan duration) => type.GetProperty(name)!.SetValue(fade, duration);

        SetDuration("Duration", TimeSpan.FromMinutes(1));
        Call(fade, "Start"); Call(fade, "Update");
        Check(Read<bool>("IsActive") && Read<double>("Opacity") == 1,
            "capture tooltip holds full opacity with a zero-length exit");
        Call(fade, "Stop");

        SetDuration("Duration", TimeSpan.Zero);
        Call(fade, "Start"); Call(fade, "Update");
        Check(!Read<bool>("IsActive") && Read<double>("Opacity") == 0,
            "a zero-duration capture fade finishes without NaN opacity or active work");

        SetDuration("FadeOutDuration", TimeSpan.FromMilliseconds(15));
        Call(fade, "Start");
        await Task.Delay(35);
        Call(fade, "Update");
        Stopwatch timer = (Stopwatch)type.BaseType!.GetProperty("Timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fade)!;
        Check(!Read<bool>("IsActive") && Read<double>("Opacity") == 0 && !timer.IsRunning,
            "finished capture tooltip fades stop both elapsed timing and transparent drawing");
        Call(fade, "Update");
        Check(!Read<bool>("IsActive") && Read<double>("Opacity") == 0,
            "subsequent capture paints keep a completed tooltip inactive");
    }
}
