// GPL-3.0-or-later. One consistent, DPI-independent stroke family for editor tools.
using System.Drawing.Drawing2D;
using ShareX.ScreenCaptureLib;

namespace ShareX.Roll;

internal static class EditorIcons
{
    internal static void Draw(Graphics g, Rectangle bounds, ShapeType? type, string name, Color ink)
    {
        GraphicsState state = g.Save();
        g.TranslateTransform(bounds.X, bounds.Y);
        g.ScaleTransform(bounds.Width / 20f, bounds.Height / 20f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using Pen p = new(ink, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using SolidBrush b = new(ink);
        void Line(float x, float y, float x2, float y2) => g.DrawLine(p, x, y, x2, y2);
        void Arrow() { Line(3, 16, 17, 3); Line(10, 3, 17, 3); Line(17, 3, 17, 10); }
        switch (type)
        {
            case ShapeType.ToolSelect:
                g.DrawPolygon(p, [new PointF(4, 2), new(5, 16), new(9, 12), new(13, 18), new(16, 16), new(12, 10), new(18, 9)]); break;
            case ShapeType.ToolCrop:
                Line(5, 2, 5, 15); Line(5, 15, 18, 15); Line(2, 5, 15, 5); Line(15, 5, 15, 18); break;
            case ShapeType.DrawingRectangle: g.DrawRectangle(p, 3, 4, 14, 12); break;
            case ShapeType.DrawingEllipse: g.DrawEllipse(p, 3, 3, 14, 14); break;
            case ShapeType.DrawingLine: Line(3, 17, 17, 3); break;
            case ShapeType.DrawingArrow: Arrow(); break;
            case ShapeType.DrawingFreehand:
            case ShapeType.DrawingFreehandArrow:
                g.DrawBezier(p, new(3, 16), new(16, 12), new(2, 5), new(16, 4));
                if (type == ShapeType.DrawingFreehandArrow) { Line(12, 2, 17, 4); Line(17, 4, 15, 8); } break;
            case ShapeType.DrawingTextOutline:
            case ShapeType.DrawingTextBackground:
                Line(5, 5, 15, 5); Line(10, 5, 10, 16); Line(7, 16, 13, 16);
                if (type == ShapeType.DrawingTextBackground) g.DrawRectangle(p, 1, 1, 18, 18); break;
            case ShapeType.DrawingSpeechBalloon:
                g.DrawPolygon(p, [new PointF(3, 3), new(17, 3), new(17, 13), new(9, 13), new(4, 18), new(4, 13), new(3, 13)]); break;
            case ShapeType.DrawingStep:
                g.DrawEllipse(p, 2, 2, 16, 16); Line(8, 7, 10, 5); Line(10, 5, 10, 15); break;
            case ShapeType.DrawingMagnify:
                g.DrawEllipse(p, 2, 2, 11, 11); Line(12, 12, 18, 18); Line(5, 7, 10, 7); Line(7.5f, 4.5f, 7.5f, 9.5f); break;
            case ShapeType.DrawingSmartEraser:
                g.DrawPolygon(p, [new PointF(3, 12), new(12, 3), new(18, 9), new(9, 18), new(7, 18)]); Line(7, 8, 13, 14); break;
            case ShapeType.EffectBlur:
                for (int y = 4; y < 18; y += 5) for (int x = 4; x < 18; x += 5) g.FillEllipse(b, x, y, 2, 2); break;
            case ShapeType.EffectPixelate:
                for (int y = 3; y < 18; y += 6) for (int x = 3; x < 18; x += 6) g.DrawRectangle(p, x, y, 4, 4); break;
            case ShapeType.EffectHighlight:
                g.DrawPolygon(p, [new PointF(4, 11), new(12, 3), new(17, 8), new(9, 16)]); Line(2, 18, 17, 18); break;
            case ShapeType.ToolSpotlight:
                g.DrawEllipse(p, 5, 5, 10, 10); Line(10, 1, 10, 3); Line(10, 17, 10, 19); Line(1, 10, 3, 10); Line(17, 10, 19, 10); break;
            case ShapeType.DrawingCursor:
                Line(6, 2, 6, 16); Line(6, 2, 16, 12); Line(6, 16, 10, 12); Line(10, 12, 16, 12); break;
            case ShapeType.DrawingSticker:
                g.DrawEllipse(p, 2, 2, 16, 16); g.FillEllipse(b, 6, 6, 2, 2); g.FillEllipse(b, 12, 6, 2, 2); g.DrawArc(p, 6, 8, 8, 6, 10, 160); break;
            case ShapeType.DrawingImage:
            case ShapeType.DrawingImageScreen:
                g.DrawRectangle(p, 2, 3, 16, 13); g.DrawLines(p, [new PointF(3, 14), new(8, 8), new(12, 12), new(15, 9), new(18, 13)]);
                if (type == ShapeType.DrawingImageScreen) Line(7, 19, 13, 19); else g.DrawEllipse(p, 12, 5, 2, 2); break;
            default:
                if (name == "editorApply") { Line(3, 10, 8, 15); Line(8, 15, 17, 5); }
                else if (name == "previewEdit")
                {
                    g.DrawPolygon(p, [new PointF(3, 17), new(4, 12), new(14, 2), new(18, 6), new(8, 16)]);
                    Line(11, 5, 15, 9); Line(4, 12, 8, 16);
                }
                else if (name == "previewSave")
                {
                    g.DrawPolygon(p, [new PointF(3, 2), new(14, 2), new(18, 6), new(18, 18), new(3, 18)]);
                    g.DrawRectangle(p, 6, 2, 7, 5); g.DrawRectangle(p, 6, 11, 9, 7);
                }
                else if (name == "settings")
                {
                    PointF[] teeth = new PointF[32];
                    for (int i = 0; i < teeth.Length; i++)
                    {
                        float angle = i * MathF.PI / 16 - MathF.PI / 2;
                        float radius = i % 4 is 1 or 2 ? 8.5f : 6.5f;
                        teeth[i] = new(10 + MathF.Cos(angle) * radius, 10 + MathF.Sin(angle) * radius);
                    }
                    g.DrawPolygon(p, teeth);
                    g.DrawEllipse(p, 7, 7, 6, 6);
                }
                else if (name == "editorStyle")
                {
                    Line(3, 5, 17, 5); Line(3, 10, 17, 10); Line(3, 15, 17, 15);
                    g.FillEllipse(b, 6, 3, 4, 4); g.FillEllipse(b, 12, 8, 4, 4); g.FillEllipse(b, 5, 13, 4, 4);
                }
                else if (name == "editorEditMenu") { g.DrawArc(p, 3, 5, 14, 12, 200, 260); Line(2, 4, 2, 10); Line(2, 10, 8, 10); }
                else if (name == "editorCopy") { g.DrawRectangle(p, 6, 6, 11, 12); Line(3, 13, 3, 2); Line(3, 2, 13, 2); }
                else { g.DrawRectangle(p, 3, 3, 14, 14); Line(10, 2, 10, 12); Line(6, 8, 10, 12); Line(10, 12, 14, 8); }
                break;
        }
        g.Restore(state);
    }
}
