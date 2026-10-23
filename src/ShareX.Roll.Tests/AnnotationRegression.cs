using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Reflection;
using System.IO;

internal static class AnnotationRegression
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static object Property(object value, string name) => value.GetType().GetProperty(name, Members)!.GetValue(value)!;
    private static object Field(object value, string name) => value.GetType().GetField(name, Members)!.GetValue(value)!;
    private static ToolStrip Tools(RegionCaptureForm editor) => editor.OwnedForms.SelectMany(form => form.Controls.OfType<ToolStrip>()).Single();
    private static void Check(bool condition, string message)
    {
        Console.WriteLine((condition ? "PASS: " : "FAIL: ") + message);
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Select(RegionCaptureForm editor, ShapeType type) => Tools(editor).Items.Cast<ToolStripItem>()
        .Single(item => Equals(item.Tag, type)).PerformClick();
    private static ToolStripDropDownButton Palette(RegionCaptureForm editor) => (ToolStripDropDownButton)Tools(editor).Items["editorAnnotationColor"]!;
    private static void ColorClick(RegionCaptureForm editor, Color color) => Palette(editor).DropDownItems.OfType<ToolStripMenuItem>()
        .Single(item => item.Tag is Color value && value.ToArgb() == color.ToArgb()).PerformClick();
    private static object? Call(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, Members)!.Invoke(target, arguments);

    internal static async Task RunAsync(MainForm main)
    {
        Directory.CreateDirectory("artifacts/image-editor");
        foreach (CapturePalette theme in new[] { CapturePalette.Light, CapturePalette.Dark })
        {
            theme.ApplyLibraryTheme();
            using Bitmap source = new(600, 400);
            using (Graphics g = Graphics.FromImage(source)) g.Clear(Color.White);
            using RegionCaptureForm editor = ScreenshotEditor.Create(source, theme);
            editor.Show(main);
            try
            {
                await Task.Delay(250);
                object manager = Property(editor, "ShapeManager");
                ToolStripDropDownButton palette = Palette(editor);
                Check(palette.Placement == ToolStripItemPlacement.Main && palette.Enabled, "color picker is directly visible when opening the editor");
                foreach (ShapeType type in new[] { ShapeType.DrawingFreehand, ShapeType.DrawingRectangle, ShapeType.DrawingEllipse,
                    ShapeType.DrawingLine, ShapeType.DrawingArrow, ShapeType.DrawingTextOutline, ShapeType.DrawingTextBackground,
                    ShapeType.DrawingSpeechBalloon, ShapeType.DrawingStep, ShapeType.EffectHighlight })
                {
                    Select(editor, type);
                    Call(manager, "DeleteAllShapes", false);
                    ColorClick(editor, Color.ForestGreen);
                    Check(editor.EditorAnnotationColor.ToArgb() == Color.ForestGreen.ToArgb(), "new annotation can start in green: " + type);
                    BaseShape shape = (BaseShape)manager.GetType().GetMethod("CreateShape", Members, null, [typeof(ShapeType)], null)!.Invoke(manager, [type])!;
                    RectangleF canvas = editor.CanvasRectangle;
                    shape.Rectangle = new(canvas.X + 100, canvas.Y + 100, 180, 100);
                    if (shape is BaseDrawingShape drawing) drawing.Shadow = false;
                    if (shape is TextDrawingShape text) text.Text = "Color sample";
                    if (shape is FreehandDrawingShape)
                    {
                        var points = (List<PointF>)Field(shape, "positions");
                        points.Add(new(canvas.X + 100, canvas.Y + 100));
                        points.Add(new(canvas.X + 280, canvas.Y + 200));
                    }
                    if (shape is LineDrawingShape line)
                    {
                        for (int i = 0; i < line.Points.Length; i++)
                        {
                            float progress = i / (float)(line.Points.Length - 1);
                            line.Points[i] = new(canvas.X + 100 + 180 * progress, canvas.Y + 100 + 100 * progress);
                        }
                        line.OnUpdate();
                    }
                    manager.GetType().GetMethod("AddShape", Members, null, [typeof(BaseShape)], null)!.Invoke(manager, [shape]);
                    ColorClick(editor, Color.Red);
                    using Bitmap red = editor.GetResultImage();
                    ColorClick(editor, Color.RoyalBlue);
                    Check(editor.EditorAnnotationColor.ToArgb() == Color.RoyalBlue.ToArgb(), "existing annotation accepts blue: " + type);
                    using Bitmap blue = editor.GetResultImage();
                    Check(!ImageHelpers.CompareImages(red, blue), "exported pixels actually change color: " + type);
                    object history = Field(manager, "history");
                    Call(history, "Undo");
                    using (Bitmap undo = editor.GetResultImage()) Check(ImageHelpers.CompareImages(red, undo), "undo restores the previous annotation color: " + type);
                    Call(history, "Redo");
                    using (Bitmap redo = editor.GetResultImage()) Check(ImageHelpers.CompareImages(blue, redo), "redo restores the new annotation color: " + type);
                }
                Select(editor, ShapeType.DrawingFreehand);
                Task custom = AcceptCustomAsync(Color.Teal, false);
                palette.DropDownItems["editorCustomColor"]!.PerformClick();
                await custom;
                Check(editor.EditorAnnotationColor.ToArgb() == Color.Teal.ToArgb(), "custom color dialog applies a color outside the quick palette");
                Task cancel = AcceptCustomAsync(Color.HotPink, true);
                palette.DropDownItems["editorCustomColor"]!.PerformClick();
                await cancel;
                Check(editor.EditorAnnotationColor.ToArgb() == Color.Teal.ToArgb(), "canceling the custom color dialog preserves the chosen color");
                editor.Size = editor.MinimumSize;
                await Task.Delay(100);
                Check(palette.Placement == ToolStripItemPlacement.Main && Tools(editor).RectangleToScreen(Tools(editor).ClientRectangle)
                    .Contains(palette.Owner!.RectangleToScreen(palette.Bounds)), "color picker remains fully visible at the minimum editor size");
                EditorRegression.Capture(editor, theme == CapturePalette.Light ? "annotation-colors-light" : "annotation-colors-dark");
            }
            finally { editor.GetType().GetMethod("CloseWindow", Members)!.Invoke(editor, [RegionResult.Close]); }
        }
        CapturePalette.Current.ApplyLibraryTheme();
    }

    private static async Task AcceptCustomAsync(Color color, bool cancel)
    {
        for (int i = 0; i < 150; i++)
        {
            await Task.Delay(25);
            ColorPickerForm? picker = Application.OpenForms.OfType<ColorPickerForm>().FirstOrDefault();
            if (picker == null) continue;
            picker.SetCurrentColor(color, true);
            picker.Controls.Find(cancel ? "btnCancel" : "btnOK", true).OfType<Button>().Single().PerformClick();
            return;
        }
        throw new TimeoutException("Custom color dialog did not open");
    }
    private static Point ScreenPoint(RegionCaptureForm editor, PointF point)
    {
        float zoom = (float)Property(editor, "ZoomFactor");
        return editor.PointToScreen(new((int)Math.Round(point.X * zoom), (int)Math.Round(point.Y * zoom)));
    }
    private static async Task DragAsync(Point start, Point end)
    {
        Check(InputHelpers.SendMouseMove(start), "real pointer reaches the annotation canvas");
        await Task.Delay(100);
        InputHelpers.SendMouseDown();
        await Task.Delay(80);
        for (int i = 1; i <= 8; i++)
        {
            InputHelpers.SendMouseMove(new(start.X + (end.X - start.X) * i / 8, start.Y + (end.Y - start.Y) * i / 8));
            await Task.Delay(35);
        }
        InputHelpers.SendMouseUp();
        await Task.Delay(150);
    }
    internal static async Task RunPhysicalAsync(MainForm main)
    {
        Directory.CreateDirectory("artifacts/image-editor");
        using Bitmap source = new(600, 400);
        using (Graphics g = Graphics.FromImage(source)) g.Clear(Color.White);
        using RegionCaptureForm editor = ScreenshotEditor.Create(source, CapturePalette.Dark);
        Point previous = Cursor.Position;
        editor.TopMost = true;
        editor.Show(main);
        try
        {
            editor.ForceActivate();
            await Task.Delay(300);
            object manager = Property(editor, "ShapeManager");
            Select(editor, ShapeType.DrawingRectangle);
            await Task.Delay(100);
            RectangleF canvas = editor.CanvasRectangle;
            await DragAsync(ScreenPoint(editor, new(canvas.X + 80, canvas.Y + 80)), ScreenPoint(editor, new(canvas.X + 220, canvas.Y + 160)));
            BaseShape rectangle = (BaseShape)Property(manager, "CurrentShape");
            Check(rectangle is RectangleDrawingShape && rectangle.Rectangle.Width > 100 && rectangle.Rectangle.Height > 60,
                "rectangle annotation can be drawn with the real pointer");
            Select(editor, ShapeType.ToolSelect);
            await Task.Delay(100);
            RectangleF before = rectangle.Rectangle;
            PointF center = new(before.X + before.Width / 2, before.Y + before.Height / 2);
            await DragAsync(ScreenPoint(editor, center), ScreenPoint(editor, new(center.X + 50, center.Y + 20)));
            Check(rectangle.Rectangle.X > before.X + 40 && rectangle.Rectangle.Y > before.Y + 10,
                "an existing annotation can be selected and moved");
            Array nodes = (Array)Property(manager, "ResizeNodes");
            PointF corner = (PointF)Property(nodes.GetValue(4)!, "Position");
            before = rectangle.Rectangle;
            await DragAsync(ScreenPoint(editor, corner), ScreenPoint(editor, new(corner.X + 40, corner.Y + 30)));
            Check(rectangle.Rectangle.Width > before.Width + 30 && rectangle.Rectangle.Height > before.Height + 20,
                "an existing annotation can be resized using its corner");
            ToolStripDropDownButton palette = Palette(editor);
            Check(palette.Placement == ToolStripItemPlacement.Main, "annotation color selection is directly visible");
            Point paletteCenter = palette.Owner!.PointToScreen(new(palette.Bounds.Left + palette.Width / 2, palette.Bounds.Top + palette.Height / 2));
            InputHelpers.SendMouseClick(paletteCenter);
            await Task.Delay(120);
            Check(palette.DropDown.Visible, "real pointer opens the quick color palette");
            ToolStripItem blue = palette.DropDownItems.Cast<ToolStripItem>().Single(item => item.Tag is Color color && color.ToArgb() == Color.RoyalBlue.ToArgb());
            InputHelpers.SendMouseClick(blue.Owner!.PointToScreen(new(blue.Bounds.Left + blue.Width / 2, blue.Bounds.Top + blue.Height / 2)));
            await Task.Delay(150);
            Check(((RectangleDrawingShape)rectangle).BorderColor.ToArgb() == Color.RoyalBlue.ToArgb(), "real pointer changes an existing annotation to blue");
            EditorRegression.Capture(editor, "annotation-editing");
        }
        finally
        {
            InputHelpers.SendMouseUp();
            InputHelpers.SendMouseMove(previous);
            editor.GetType().GetMethod("CloseWindow", Members)!.Invoke(editor, [RegionResult.Close]);
        }
    }
}
