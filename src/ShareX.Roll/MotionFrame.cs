// GPL-3.0-or-later. Temporary, opaque display pixels; never owns the captured source image.
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ShareX.Roll;

internal sealed class MotionFrame : IDisposable
{
    private int[]? pixels;
    private Bitmap? bitmap;
    internal Size Size { get; private set; }

    internal MotionFrame(Size size)
    {
        try { Initialize(size); }
        catch { Dispose(); throw; }
    }

    // Takes ownership of the display copy, never of PreviewCanvas.Image.
    internal MotionFrame(Bitmap snapshot)
    {
        try
        {
            Initialize(snapshot.Size);
            BitmapData data = snapshot.LockBits(new Rectangle(Point.Empty, snapshot.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                if (data.Stride == snapshot.Width * 4) Marshal.Copy(data.Scan0, pixels!, 0, pixels!.Length);
                else for (int y = 0; y < snapshot.Height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, checked(y * data.Stride)), pixels!, y * snapshot.Width, snapshot.Width);
            }
            finally { snapshot.UnlockBits(data); }
            // All inputs are already rendered on an opaque page/canvas. Native snapshots may leave alpha unset.
            Span<uint> values = MemoryMarshal.Cast<int, uint>(pixels.AsSpan());
            int i = 0;
            Vector<uint> opaque = new(0xff000000u);
            if (Vector.IsHardwareAccelerated)
                for (; i <= values.Length - Vector<uint>.Count; i += Vector<uint>.Count)
                    (new Vector<uint>(values.Slice(i)) | opaque).CopyTo(values.Slice(i));
            for (; i < values.Length; i++) values[i] |= 0xff000000u;
        }
        catch { Dispose(); throw; }
        finally { snapshot.Dispose(); }
    }

    private void Initialize(Size size)
    {
        Size = size;
        // POH storage remains at a stable address for the lifetime of the Bitmap view.
        pixels = GC.AllocateUninitializedArray<int>(checked(size.Width * size.Height), pinned: true);
        bitmap = new Bitmap(size.Width, size.Height, checked(size.Width * 4), PixelFormat.Format32bppPArgb,
            Marshal.UnsafeAddrOfPinnedArrayElement(pixels, 0));
    }

    internal void Compose(Graphics graphics, Color background, MotionFrame first, int firstX = 0, float firstOpacity = 1,
        MotionFrame? second = null, int secondX = 0, float secondOpacity = 1)
    {
        if (bitmap == null || pixels == null) return;
        pixels.AsSpan().Fill(background.ToArgb());
        Blend(first, firstX, firstOpacity);
        if (second != null) Blend(second, secondX, secondOpacity);
        graphics.DrawImageUnscaled(bitmap, 0, 0);
        GC.KeepAlive(pixels);
    }

    private void Blend(MotionFrame source, int x, float opacity)
    {
        if (source.pixels == null || pixels == null) return;
        uint alpha = (uint)Math.Clamp((int)Math.Round(opacity * 256), 0, 256);
        if (alpha == 0) return;
        int sourceX = Math.Max(0, -x), destinationX = Math.Max(0, x);
        int width = Math.Min(source.Size.Width - sourceX, Size.Width - destinationX);
        int height = Math.Min(source.Size.Height, Size.Height);
        if (width <= 0) return;
        Vector<uint> weight = new(alpha), inverse = new(256 - alpha);
        Vector<uint> rbMask = new(0x00ff00ffu), greenMask = new(0xffu), rbRound = new(0x00800080u), round = new(128u), opaque = new(0xff000000u);
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<uint> input = MemoryMarshal.Cast<int, uint>(source.pixels.AsSpan(y * source.Size.Width + sourceX, width));
            Span<uint> output = MemoryMarshal.Cast<int, uint>(pixels.AsSpan(y * Size.Width + destinationX, width));
            if (alpha == 256) { input.CopyTo(output); continue; }
            int i = 0;
            if (Vector.IsHardwareAccelerated)
                for (; i <= width - Vector<uint>.Count; i += Vector<uint>.Count)
                {
                    Vector<uint> from = new(input.Slice(i)), to = new(output.Slice(i));
                    Vector<uint> rb = Vector.ShiftRightLogical((from & rbMask) * weight + (to & rbMask) * inverse + rbRound, 8) & rbMask;
                    Vector<uint> green = Vector.ShiftRightLogical((Vector.ShiftRightLogical(from, 8) & greenMask) * weight
                        + (Vector.ShiftRightLogical(to, 8) & greenMask) * inverse + round, 8);
                    (rb | Vector.ShiftLeft(green, 8) | opaque).CopyTo(output.Slice(i));
                }
            for (; i < width; i++)
            {
                uint from = input[i], to = output[i];
                uint rb = (((from & 0x00ff00ffu) * alpha + (to & 0x00ff00ffu) * (256 - alpha) + 0x00800080u) >> 8) & 0x00ff00ffu;
                uint green = ((((from >> 8) & 255) * alpha + ((to >> 8) & 255) * (256 - alpha) + 128) >> 8) << 8;
                output[i] = rb | green | 0xff000000u;
            }
        }
    }

    public void Dispose()
    {
        bitmap?.Dispose();
        bitmap = null;
        pixels = null;
    }
}
