using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class StitchingRegression
{
    // Independent pixel ground truth, including the narrower final wheel step.
    internal static void Run()
    {
        const int width = 1600, height = 3800, viewport = 1000;
        using Bitmap document = new(width, height, PixelFormat.Format32bppArgb);
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int at = (y * width + x) * 4;
                pixels[at] = (byte)(x * 7 + y * 13 + (x * y % 53));
                pixels[at + 1] = (byte)(x * 11 + y * 17 + (x * y % 37));
                pixels[at + 2] = (byte)(x * 19 + y * 23 + (x * y % 71));
                pixels[at + 3] = 255;
            }
        BitmapData data = document.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        document.UnlockBits(data);

        Type managerType = typeof(ScrollingCaptureForm).Assembly.GetType("ShareX.ScreenCaptureLib.ScrollingCaptureManager")!;
        MethodInfo combine = managerType.GetMethod("CombineImagesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (int overlayWidth in new[] { 0, 190, 310 })
        {
            using IDisposable manager = (IDisposable)Activator.CreateInstance(managerType, new ScrollingCaptureOptions())!;
            Bitmap? result = null;
            try
            {
                // 260 * 10 + 200 exercises a final step smaller than previous steps.
                foreach (int offset in Enumerable.Range(0, 11).Select(i => i * 260).Append(height - viewport))
                {
                    using Bitmap frame = document.Clone(new Rectangle(0, offset, width, viewport), PixelFormat.Format32bppArgb);
                    DrawControls(frame, overlayWidth);
                    Task<Bitmap> task = (Task<Bitmap>)combine.Invoke(manager, new object?[] { result, frame })!;
                    Bitmap? next = task.GetAwaiter().GetResult();
                    if (next == null) throw new InvalidOperationException($"Failed to stitch {overlayWidth}px overlay at {offset}");
                    result?.Dispose(); result = next;
                }
                if (result!.Size != document.Size) throw new InvalidOperationException("Stitching changed document geometry");
                using Bitmap expected = (Bitmap)document.Clone();
                if (overlayWidth > 0)
                {
                    using Graphics g = Graphics.FromImage(expected);
                    PaintControls(g, overlayWidth, width, height - viewport);
                }
                if (!ImageHelpers.CompareImages(result, expected)) throw new InvalidOperationException($"Content lost under {overlayWidth}px fixed controls");
                Console.WriteLine($"PASS: {overlayWidth}px fixed controls; complete document pixels and final step retained");
            }
            finally { result?.Dispose(); }
        }
    }

    private static void DrawControls(Bitmap frame, int width)
    {
        if (width == 0) return;
        using Graphics g = Graphics.FromImage(frame);
        PaintControls(g, width, frame.Width, 0);
    }

    private static void PaintControls(Graphics g, int width, int viewportWidth, int offset)
    {
        g.FillRectangle(Brushes.DarkBlue, viewportWidth - width, offset + 650, width, 300);
        g.FillRectangle(Brushes.DarkRed, 0, offset + 650, width, 300);
    }
}
