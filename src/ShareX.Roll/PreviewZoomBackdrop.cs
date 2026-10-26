// GPL-3.0-or-later. Reuse a small, padded image sample for the zoom toolbar's frosted surface.
using ShareX.HelpersLib;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ShareX.Roll;

internal sealed class PreviewZoomBackdrop : IDisposable
{
    private Bitmap? sample;
    private Bitmap? material;
    private Color materialTint;
    private Rectangle bounds;
    private int padding;
    private bool dirty = true;

    internal void Invalidate() => dirty = true;

    internal void Paint(Graphics graphics, PreviewCanvas viewport, Rectangle toolbarBounds, GraphicsPath outline, Color tint)
    {
        if (bounds != toolbarBounds) { bounds = toolbarBounds; dirty = true; }
        bool refresh = dirty || sample == null;
        if (refresh)
        {
            float dpi = viewport.DeviceDpi / 96f;
            padding = (int)Math.Ceiling(40 * dpi);
            Rectangle area = bounds;
            area.Inflate(padding, padding);
            Size size = new(Math.Max(1, (int)Math.Ceiling(area.Width / (4 * dpi))),
                Math.Max(1, (int)Math.Ceiling(area.Height / (4 * dpi))));
            if (sample == null || sample.Size != size)
            {
                sample?.Dispose();
                sample = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            }
            using (Graphics target = Graphics.FromImage(sample))
            {
                target.ScaleTransform((float)size.Width / area.Width, (float)size.Height / area.Height);
                viewport.PaintZoomBackdrop(target, area);
            }
            // Two box passes at quarter resolution soften text without processing the full screenshot.
            ImageHelpers.FastBoxBlur(sample, 4);
            ImageHelpers.FastBoxBlur(sample, 4);
            dirty = false;
        }
        if (material == null || material.Size != bounds.Size)
        {
            material?.Dispose();
            material = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            refresh = true;
        }
        if (refresh || materialTint != tint)
        {
            using Graphics target = Graphics.FromImage(material);
            target.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using ImageAttributes edges = new();
            edges.SetWrapMode(WrapMode.TileFlipXY);
            target.DrawImage(sample!, new Rectangle(-padding, -padding, bounds.Width + 2 * padding, bounds.Height + 2 * padding),
                0, 0, sample!.Width, sample.Height, GraphicsUnit.Pixel, edges);
            using SolidBrush veil = new(tint);
            target.FillRectangle(veil, new Rectangle(Point.Empty, bounds.Size));
            materialTint = tint;
        }
        // FillPath blends the material and tint together at the rounded edge. Window regions
        // and SetClip paths reduce that edge to a hard pixel mask, even with AntiAlias enabled.
        using TextureBrush surface = new(material, WrapMode.Clamp);
        graphics.FillPath(surface, outline);
    }

    public void Dispose() { sample?.Dispose(); material?.Dispose(); sample = material = null; }
}
