// GPL-3.0-or-later. Material coverage for the existing page animation, separate from its image pixels.
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ShareX.Roll;

internal sealed class BackdropSnapshot
{
    internal readonly Size Size;
    internal readonly uint[] Pixels;
    internal readonly uint Background;

    internal BackdropSnapshot(Size size, uint background)
    {
        Size = size;
        Background = background;
        Pixels = new uint[checked(size.Width * size.Height)];
    }

    // Subtract this premultiplied matte from the opaque drawing to expose the native material.
    internal static uint RemovedMatte(CapturePalette palette)
    {
        uint transmission = palette.Background.GetBrightness() < .5 ? 31u : 79u;
        Color color = palette.Background;
        return transmission << 24 | ((color.R * transmission + 127) / 255) << 16
            | ((color.G * transmission + 127) / 255) << 8 | (color.B * transmission + 127) / 255;
    }

    internal static uint CoveredMatte(uint matte, uint coverage) =>
        (((matte >> 24) * coverage + 127) / 255) << 24 |
        ((((matte >> 16) & 255) * coverage + 127) / 255) << 16 |
        ((((matte >> 8) & 255) * coverage + 127) / 255) << 8 |
        ((matte & 255) * coverage + 127) / 255;

    // Keep text and icon edges opaque if any ClearType channel contains foreground ink.
    internal static uint Coverage(uint pixel) => Math.Min((pixel >> 16) & 255, Math.Min((pixel >> 8) & 255, pixel & 255));

    internal static void RemoveButtonMatte(Span<uint> colors, ReadOnlySpan<int> coverage, uint matte)
    {
        for (int i = 0; i < colors.Length; i++) colors[i] = RemoveMatte(colors[i], CoveredMatte(matte, Coverage((uint)coverage[i])));
    }

    internal static BackdropSnapshot Capture(Control page, Bitmap image, CapturePalette palette, Point origin = default)
    {
        BackdropSnapshot result = new(image.Size, RemovedMatte(palette));
        int[] colors = new int[result.Pixels.Length];
        BitmapData data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < image.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), colors, y * image.Width, image.Width);
        }
        finally { image.UnlockBits(data); }
        int background = palette.Background.ToArgb() & 0xFFFFFF;
        ReadOnlySpan<uint> input = MemoryMarshal.Cast<int, uint>(colors.AsSpan());
        Vector<uint> rgb = new(0xFFFFFF), key = new((uint)background), matte = new(result.Background);
        int pixel = 0;
        if (Vector.IsHardwareAccelerated)
            for (; pixel <= input.Length - Vector<uint>.Count; pixel += Vector<uint>.Count)
                Vector.ConditionalSelect(Vector.Equals(new Vector<uint>(input.Slice(pixel)) & rgb, key), matte, Vector<uint>.Zero)
                    .CopyTo(result.Pixels.AsSpan(pixel));
        for (; pixel < input.Length; pixel++)
            if ((input[pixel] & 0xFFFFFF) == (uint)background) result.Pixels[pixel] = result.Background;
        void ProtectImages(Control control, Point origin, Rectangle clip)
        {
            Rectangle area = Rectangle.Intersect(new Rectangle(origin, control.ClientSize), clip);
            if (area.Width <= 0 || area.Height <= 0) return;
            // Match live rendering: only the preview frame itself may expose its rounded outside corners.
            if (control is PictureBox || control.Parent is PreviewSurface)
            {
                for (int y = area.Top; y < area.Bottom; y++)
                    result.Pixels.AsSpan(y * image.Width + area.Left, area.Width).Clear();
                return;
            }
            if (CaptureButtonPainter.HasBackdrop(control))
            {
                using Bitmap mask = new(control.Width, control.Height, PixelFormat.Format32bppArgb);
                mask.SetResolution(control.DeviceDpi, control.DeviceDpi);
                using (Graphics graphics = Graphics.FromImage(mask)) CaptureButtonPainter.PaintBackdrop(control, graphics);
                int[] row = new int[area.Width];
                BitmapData maskData = mask.LockBits(new Rectangle(Point.Empty, mask.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = area.Top; y < area.Bottom; y++)
                    {
                        Marshal.Copy(IntPtr.Add(maskData.Scan0, (y - origin.Y) * maskData.Stride + (area.Left - origin.X) * 4), row, 0, row.Length);
                        for (int x = 0; x < row.Length; x++)
                            result.Pixels[y * image.Width + area.Left + x] = CoveredMatte(result.Background, Coverage((uint)row[x]));
                    }
                }
                finally { mask.UnlockBits(maskData); }
                return;
            }
            foreach (Control child in control.Controls)
                if (child.Visible) ProtectImages(child, new Point(origin.X + child.Left, origin.Y + child.Top), area);
        }
        ProtectImages(page, origin, new Rectangle(Point.Empty, image.Size));
        // Only pages with overlapping material layers supply an override; other pages keep their masks.
        if (page is IBackdropSnapshotOverlay overlay) overlay.ApplySnapshotOverlay(result);
        return result;
    }

    internal void Compose(BackdropSnapshot before, int beforeX, BackdropSnapshot? after, int afterX, float opacity)
    {
        Pixels.AsSpan().Fill(Background);
        Blend(before, beforeX, 256);
        if (after != null) Blend(after, afterX, (uint)Math.Clamp((int)Math.Round(opacity * 256), 0, 256));
    }

    private void Blend(BackdropSnapshot source, int offset, uint alpha)
    {
        if (alpha == 0) return;
        int sourceX = Math.Max(0, -offset), targetX = Math.Max(0, offset);
        int width = Math.Min(source.Size.Width - sourceX, Size.Width - targetX);
        int height = Math.Min(source.Size.Height, Size.Height);
        if (width <= 0) return;
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<uint> input = source.Pixels.AsSpan(y * source.Size.Width + sourceX, width);
            Span<uint> output = Pixels.AsSpan(y * Size.Width + targetX, width);
            if (alpha == 256) { input.CopyTo(output); continue; }
            int x = 0;
            Vector<uint> weight = new(alpha), inverse = new(256 - alpha), mask = new(0x00FF00FF), round = new(0x00800080);
            if (Vector.IsHardwareAccelerated)
                for (; x <= width - Vector<uint>.Count; x += Vector<uint>.Count)
                {
                    Vector<uint> from = new(input.Slice(x)), to = new(output.Slice(x));
                    Vector<uint> rb = Vector.ShiftRightLogical((from & mask) * weight + (to & mask) * inverse + round, 8) & mask;
                    Vector<uint> ag = Vector.ShiftLeft(Vector.ShiftRightLogical((Vector.ShiftRightLogical(from, 8) & mask) * weight
                        + (Vector.ShiftRightLogical(to, 8) & mask) * inverse + round, 8) & mask, 8);
                    (rb | ag).CopyTo(output.Slice(x));
                }
            for (; x < width; x++)
            {
                uint from = input[x], to = output[x];
                uint rb = (((from & 0x00FF00FF) * alpha + (to & 0x00FF00FF) * (256 - alpha) + 0x00800080) >> 8) & 0x00FF00FF;
                uint ag = (((((from >> 8) & 0x00FF00FF) * alpha + ((to >> 8) & 0x00FF00FF) * (256 - alpha) + 0x00800080) >> 8) & 0x00FF00FF) << 8;
                output[x] = rb | ag;
            }
        }
    }

    internal BackdropSnapshot Copy()
    {
        BackdropSnapshot copy = new(Size, Background);
        Pixels.CopyTo(copy.Pixels, 0);
        return copy;
    }

    internal static uint RemoveMatte(uint color, uint matte)
    {
        uint red = (color >> 16) & 255, green = (color >> 8) & 255, blue = color & 255;
        return (255 - (matte >> 24)) << 24 | (red - Math.Min(red, (matte >> 16) & 255)) << 16
            | (green - Math.Min(green, (matte >> 8) & 255)) << 8 | (blue - Math.Min(blue, matte & 255));
    }

    internal static void RemoveMatte(Span<uint> colors, ReadOnlySpan<uint> mattes)
    {
        Vector<uint> mask = new(255);
        int i = 0;
        if (Vector.IsHardwareAccelerated)
            for (; i <= colors.Length - Vector<uint>.Count; i += Vector<uint>.Count)
            {
                Vector<uint> color = new(colors.Slice(i)), matte = new(mattes.Slice(i));
                Vector<uint> red = Vector.ShiftRightLogical(color, 16) & mask, green = Vector.ShiftRightLogical(color, 8) & mask, blue = color & mask;
                red -= Vector.Min(red, Vector.ShiftRightLogical(matte, 16) & mask);
                green -= Vector.Min(green, Vector.ShiftRightLogical(matte, 8) & mask);
                blue -= Vector.Min(blue, matte & mask);
                (Vector.ShiftLeft(mask - Vector.ShiftRightLogical(matte, 24), 24) | Vector.ShiftLeft(red, 16)
                    | Vector.ShiftLeft(green, 8) | blue).CopyTo(colors.Slice(i));
            }
        for (; i < colors.Length; i++) colors[i] = RemoveMatte(colors[i], mattes[i]);
    }
}
