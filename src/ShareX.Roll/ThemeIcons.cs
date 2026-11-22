// GPL-3.0-or-later. Icon sources and license notices accompany the embedded masks.
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO.Compression;
using ShareX.ScreenCaptureLib;

namespace ShareX.Roll;

internal static class ThemeIcons
{
    private static readonly Lazy<IconSet> icons = new(Load);
    private static readonly Lazy<FontFamily?> metroFont = new(() =>
    {
        try { return new FontFamily("Segoe MDL2 Assets"); }
        catch (ArgumentException) { return null; }
    });

    private readonly record struct Glyph(char Symbol, float Angle);
    private sealed record IconSet(Dictionary<string, Bitmap> Masks, Dictionary<string, Glyph> Metro);

    private static IconSet Load()
    {
        using Stream stream = typeof(ThemeIcons).Assembly.GetManifestResourceStream("SnapFlow.ThemeIcons")!;
        using ZipArchive source = new(stream);
        Dictionary<string, Bitmap> masks = new(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in source.Entries.Where(entry => entry.Name.EndsWith(".png", StringComparison.Ordinal)))
        {
            using Stream data = entry.Open();
            using MemoryStream image = new();
            data.CopyTo(image);
            image.Position = 0;
            using Bitmap decoded = new(image);
            masks.Add(entry.FullName[..^4], new Bitmap(decoded));
        }

        using StreamReader codes = new(source.GetEntry("MetroClassic.tsv")!.Open());
        Dictionary<string, Glyph> metro = new(StringComparer.Ordinal);
        while (codes.ReadLine() is { } row)
        {
            string[] fields = row.Split('\t');
            metro.Add(fields[0], new((char)int.Parse(fields[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                float.Parse(fields[2], CultureInfo.InvariantCulture)));
        }
        return new(masks, metro);
    }

    internal static RectangleF Centered(float x, float y, float size) => new(x - size / 2, y - size / 2, size, size);

    internal static bool TryDraw(Graphics graphics, RectangleF bounds, SoftwareStyle style, string? action, Color ink)
    {
        if (style == SoftwareStyle.Default || action == null || bounds.Width <= 0 || bounds.Height <= 0) return false;
        IconSet set = icons.Value;
        if (style == SoftwareStyle.MetroClassic && metroFont.Value is { } font && set.Metro.TryGetValue(action, out Glyph glyph))
        {
            using GraphicsPath path = new();
            using StringFormat format = StringFormat.GenericTypographic;
            float size = Math.Min(bounds.Width, bounds.Height);
            path.AddString(glyph.Symbol.ToString(), font, (int)FontStyle.Regular, size * .83f, PointF.Empty, format);
            using Matrix position = new();
            if (glyph.Angle != 0)
            {
                position.Rotate(glyph.Angle);
                path.Transform(position);
                position.Reset();
            }
            RectangleF shape = path.GetBounds();
            if (!shape.IsEmpty)
            {
                position.Translate(bounds.Left + (bounds.Width - shape.Width) / 2 - shape.Left,
                    bounds.Top + (bounds.Height - shape.Height) / 2 - shape.Top);
                path.Transform(position);
                GraphicsState state = graphics.Save();
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using SolidBrush brush = new(ink);
                graphics.FillPath(brush, path);
                graphics.Restore(state);
                return true;
            }
        }

        // Use the bundled Microsoft icon for specialized tools missing from
        // MDL2 Assets and on systems where that Windows font is unavailable.
        SoftwareStyle maskStyle = style == SoftwareStyle.MetroClassic ? SoftwareStyle.WinUI : style;
        if (!set.Masks.TryGetValue($"{maskStyle}/{action}", out Bitmap? mask)) return false;
        using ImageAttributes tint = new();
        tint.SetColorMatrix(new ColorMatrix
        {
            Matrix00 = 0, Matrix11 = 0, Matrix22 = 0, Matrix33 = ink.A / 255f,
            Matrix40 = ink.R / 255f, Matrix41 = ink.G / 255f, Matrix42 = ink.B / 255f,
        });
        GraphicsState previous = graphics.Save();
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(mask, [bounds.Location, new PointF(bounds.Right, bounds.Top), new PointF(bounds.Left, bounds.Bottom)],
            new RectangleF(0, 0, mask.Width, mask.Height), GraphicsUnit.Pixel, tint);
        graphics.Restore(previous);
        return true;
    }

    internal static string? EditorAction(ShapeType? type, string name) => type switch
    {
        ShapeType.ToolSelect => "select",
        ShapeType.ToolCrop => "crop",
        ShapeType.DrawingRectangle => "rectangle",
        ShapeType.DrawingEllipse => "ellipse",
        ShapeType.DrawingLine => "line",
        ShapeType.DrawingArrow => "arrow",
        ShapeType.DrawingFreehand => "freehand",
        ShapeType.DrawingFreehandArrow => "freehandArrow",
        ShapeType.DrawingTextOutline => "text",
        ShapeType.DrawingTextBackground => "textBox",
        ShapeType.DrawingSpeechBalloon => "speech",
        ShapeType.DrawingStep => "step",
        ShapeType.DrawingMagnify => "magnify",
        ShapeType.DrawingSmartEraser => "erase",
        ShapeType.EffectBlur => "blur",
        ShapeType.EffectPixelate => "pixelate",
        ShapeType.EffectHighlight => "highlight",
        ShapeType.ToolSpotlight => "spotlight",
        ShapeType.DrawingCursor => "pointer",
        ShapeType.DrawingSticker => "sticker",
        ShapeType.DrawingImage => "image",
        ShapeType.DrawingImageScreen => "screen",
        _ => name switch
        {
            "editorApply" => "ok",
            "previewEdit" => "edit",
            "editorSave" or "previewSave" => "save",
            "editorSaveAs" => "saveAs",
            "editorCopy" => "copy",
            "settings" or "tsbEditStickers" => "settings",
            "editorStyle" => "options",
            "editorEditMenu" => "editMenu",
            "editorUndo" => "undo",
            "editorRedo" => "redo",
            "editorPaste" => "paste",
            "editorDuplicate" => "duplicate",
            "editorDelete" => "delete",
            "editorDeleteAll" => "deleteAll",
            "editorMoveTop" => "top",
            "editorMoveUp" => "up",
            "editorMoveDown" => "down",
            "editorMoveBottom" => "bottom",
            _ => null,
        }
    };
}
