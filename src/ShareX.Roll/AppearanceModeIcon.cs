// GPL-3.0-or-later. The miniature illustrations for the settings color-mode choices.
using System.Drawing.Drawing2D;

namespace ShareX.Roll;

internal static class AppearanceModeIcon
{
    internal static void Draw(Graphics graphics, RectangleF bounds, CapturePalette palette, AppearanceMode mode)
    {
        GraphicsState state = graphics.Save();
        try
        {
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 54, bounds.Height / 40);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            CapturePalette front = CapturePalette.For(mode == AppearanceMode.Dark ? AppearanceMode.Dark : AppearanceMode.Light, palette.Style);
            CapturePalette alternate = mode == AppearanceMode.System ? CapturePalette.For(AppearanceMode.Dark, palette.Style) : front;
            switch (palette.Style)
            {
                case SoftwareStyle.Claude: DrawPaper(graphics, front, alternate, mode); break;
                case SoftwareStyle.WinUI: DrawPanes(graphics, front, alternate, mode == AppearanceMode.System); break;
                case SoftwareStyle.MetroClassic: DrawTiles(graphics, front, alternate); break;
                case SoftwareStyle.MaterialYou: DrawMaterial(graphics, front, mode); break;
            }
        }
        finally { graphics.Restore(state); }
    }

    private static void DrawPaper(Graphics graphics, CapturePalette front, CapturePalette back, AppearanceMode mode)
    {
        if (mode == AppearanceMode.System)
        {
            using SolidBrush backing = new(back.Surface);
            using Pen backingEdge = new(back.FieldBorder, 1.1f) { LineJoin = LineJoin.Round };
            PointF[] rear = [new(20, 7), new(43, 4), new(46, 31), new(22, 34)];
            graphics.FillPolygon(backing, rear);
            graphics.DrawPolygon(backingEdge, rear);
        }

        using SolidBrush paper = new(front.Canvas);
        using Pen edge = new(front.FieldBorder, 1.1f) { LineJoin = LineJoin.Round, EndCap = LineCap.Round };
        PointF[] sheet = [new(13, 6), new(34, 6), new(40, 12), new(40, 35), new(13, 35)];
        graphics.FillPolygon(paper, sheet);
        graphics.DrawPolygon(edge, sheet);
        graphics.DrawLines(edge, [new PointF(34, 6), new PointF(34, 12), new PointF(40, 12)]);
        graphics.DrawLine(edge, 19, 29, 34, 29);
        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(12.5f, 8.5f);
        graphics.ScaleTransform(.5f, .5f);
        DrawModeGlyph(graphics, front.IllustrationAccent, mode, strokeWidth: 2.35f);
        graphics.Restore(state);
    }

    private static void DrawPanes(Graphics graphics, CapturePalette front, CapturePalette alternate, bool system)
    {
        RectangleF frame = new(7, 6, 40, 28);
        using GraphicsPath window = CaptureDrawing.Rounded(frame, CapturePalette.WinUIControlRadius);
        using SolidBrush chrome = new(front.Surface);
        using SolidBrush content = new(alternate.Canvas);
        using Pen detail = new(front.FieldBorder, 1.15f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using Pen indicator = new(alternate.Accent, 1.55f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.FillPath(chrome, window);
        graphics.DrawLine(detail, 8, 13, 46, 13);
        if (system)
        {
            graphics.FillRectangle(content, 27, 14, 18, 18);
            graphics.DrawLine(detail, 27, 14, 27, 32);
        }
        graphics.DrawLine(indicator, system ? 32 : 14, 23, system ? 40 : 30, 23);
        CaptureDrawing.DrawInsideBorder(graphics, frame, CapturePalette.WinUIControlRadius, front.FieldBorder, 1);
    }

    private static void DrawTiles(Graphics graphics, CapturePalette front, CapturePalette alternate)
    {
        using SolidBrush accent = new(CaptureDrawing.Blend(front.Accent, front.AccentText, .16f));
        using SolidBrush caption = new(front.AccentText);
        using SolidBrush tile = new(CaptureDrawing.Blend(alternate.Surface, alternate.Text, .22f));
        using SolidBrush ink = new(alternate.Text);
        graphics.FillRectangle(accent, 6, 6, 24, 28);
        graphics.FillRectangle(tile, 32, 6, 16, 13);
        graphics.FillRectangle(tile, 32, 21, 16, 13);
        graphics.FillRectangle(caption, 14, 16, 8, 8);
        graphics.FillRectangle(ink, 37, 11, 6, 2);
        graphics.FillRectangle(ink, 37, 26, 6, 2);
    }

    private static void DrawMaterial(Graphics graphics, CapturePalette colors, AppearanceMode mode)
    {
        using GraphicsPath container = CaptureDrawing.Rounded(new(10, 3, 34, 34), 10);
        using SolidBrush fill = new(CaptureDrawing.Blend(colors.Tint, colors.TintText, .12f));
        graphics.FillPath(fill, container);
        DrawModeGlyph(graphics, colors.TintText, mode, strokeWidth: 2.0f);
    }

    private static void DrawModeGlyph(Graphics graphics, Color color, AppearanceMode mode, float strokeWidth)
    {
        const float centerX = 27, centerY = 20;
        using SolidBrush ink = new(color);
        using Pen stroke = new(color, strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (mode)
        {
            case AppearanceMode.Light:
                graphics.DrawEllipse(stroke, centerX - 5.5f, centerY - 5.5f, 11, 11);
                for (int ray = 0; ray < 8; ray++)
                {
                    float angle = ray * MathF.PI / 4;
                    float x = MathF.Cos(angle), y = MathF.Sin(angle);
                    graphics.DrawLine(stroke, centerX + 9 * x, centerY + 9 * y, centerX + 12 * x, centerY + 12 * y);
                }
                break;
            case AppearanceMode.Dark:
                using (GraphicsPath moon = new())
                {
                    moon.AddBezier(27, 8, 20.2f, 8, 15.5f, 13.6f, 15.5f, 20.2f);
                    moon.AddBezier(15.5f, 20.2f, 15.5f, 27.2f, 21, 32.3f, 28, 32.3f);
                    moon.AddBezier(28, 32.3f, 33.8f, 32.3f, 38.4f, 28.1f, 39.5f, 22.9f);
                    moon.AddBezier(39.5f, 22.9f, 32.7f, 26.4f, 21.6f, 19.7f, 27, 8);
                    moon.CloseFigure();
                    graphics.FillPath(ink, moon);
                }
                break;
            default:
                RectangleF disc = new(centerX - 11.5f, centerY - 11.5f, 23, 23);
                graphics.FillPie(ink, disc.X, disc.Y, disc.Width, disc.Height, 90, 180);
                graphics.DrawEllipse(stroke, disc);
                break;
        }
    }

}
