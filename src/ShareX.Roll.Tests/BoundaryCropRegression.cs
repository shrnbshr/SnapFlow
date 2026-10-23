using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Reflection;
using System.IO;

internal static class BoundaryCropRegression
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static bool simulateInput;
    private static object Property(object target, string name) => target.GetType().GetProperty(name, Members)!.GetValue(target)!;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Members)!.GetValue(target)!;
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Members)!.Invoke(target, args);
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private static ToolStrip Toolbar(RegionCaptureForm editor) => editor.OwnedForms.SelectMany(f => f.Controls.OfType<ToolStrip>()).Single();
    private static CropTool Crop(object manager) => (CropTool)Property(manager, "CurrentShape");
    private static bool SameArea(RectangleF a, RectangleF b) => Math.Abs(a.X - b.X) < .01f && Math.Abs(a.Y - b.Y) < .01f &&
        Math.Abs(a.Width - b.Width) < .01f && Math.Abs(a.Height - b.Height) < .01f;

    private static async Task StartCropAsync(RegionCaptureForm editor)
    {
        ToolStrip toolbar = Toolbar(editor);
        if (simulateInput)
        {
            toolbar.Items["editorCrop"]!.PerformClick();
            await Task.Delay(100);
            return;
        }
        Rectangle bounds = toolbar.Items["editorCrop"]!.Bounds;
        Point point = toolbar.PointToScreen(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
        InputHelpers.SendMouseClick(point);
        await Task.Delay(150);
    }

    private static async Task DragAsync(RegionCaptureForm editor, object manager, int nodeIndex, int dx, int dy)
    {
        Array nodes = (Array)Property(manager, "ResizeNodes");
        PointF position = (PointF)Property(nodes.GetValue(nodeIndex)!, "Position");
        float zoom = (float)Property(editor, "ZoomFactor");
        Point start = editor.PointToScreen(new Point((int)Math.Round(position.X * zoom), (int)Math.Round(position.Y * zoom)));
        Point end = new(start.X + (int)Math.Round(dx * zoom), start.Y + (int)Math.Round(dy * zoom));
        if (simulateInput)
        {
            object input = Property(manager, "InputManager");
            MouseState State(Point client, MouseButtons buttons)
            {
                object boxed = new MouseState();
                typeof(MouseState).GetProperty("Buttons")!.SetValue(boxed, buttons);
                typeof(MouseState).GetProperty("ClientPosition")!.SetValue(boxed, client);
                typeof(MouseState).GetProperty("Position")!.SetValue(boxed, editor.PointToScreen(client));
                return (MouseState)boxed;
            }
            void SetInput(MouseState previous, MouseState current)
            {
                input.GetType().GetField("oldMouseState", Members)!.SetValue(input, previous);
                input.GetType().GetField("mouseState", Members)!.SetValue(input, current);
            }
            Point clientStart = editor.PointToClient(start), clientEnd = editor.PointToClient(end);
            void UpdateHitTargets() => manager.GetType().GetMethod("UpdateObjects", Members, null, Type.EmptyTypes, null)!.Invoke(manager, null);
            SetInput(State(clientStart, MouseButtons.None), State(clientStart, MouseButtons.Left));
            UpdateHitTargets();
            Check((bool)Property(nodes.GetValue(nodeIndex)!, "IsDragging"), "pointer hit testing starts the requested crop handle drag");
            Call(manager, "Update");
            SetInput(State(clientStart, MouseButtons.Left), State(clientEnd, MouseButtons.Left));
            UpdateHitTargets();
            Call(manager, "Update");
            SetInput(State(clientEnd, MouseButtons.Left), State(clientEnd, MouseButtons.None));
            UpdateHitTargets();
            Call(manager, "Update");
            Call(input, "Update", editor);
            editor.Invalidate();
            await Task.Delay(100);
            return;
        }
        InputHelpers.SendMouseMove(start);
        await Task.Delay(100);
        InputHelpers.SendMouseDown();
        await Task.Delay(100);
        for (int step = 1; step <= 6; step++)
        {
            InputHelpers.SendMouseMove(new Point(start.X + (end.X - start.X) * step / 6, start.Y + (end.Y - start.Y) * step / 6));
            await Task.Delay(40);
        }
        InputHelpers.SendMouseUp();
        await Task.Delay(180);
    }

    private static void MatchPixels(Bitmap original, Bitmap result, Rectangle area)
    {
        Check(result.Size == area.Size, "confirmed crop uses the adjusted image boundaries");
        for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < result.Width; x++)
                if (result.GetPixel(x, y) != original.GetPixel(area.X + x, area.Y + y))
                    throw new InvalidOperationException("Boundary crop pixel mismatch");
        Check(true, "every boundary-crop pixel matches the original image");
    }

    internal static async Task RunViewportAsync(MainForm main)
    {
        simulateInput = true;
        Directory.CreateDirectory("artifacts/image-editor");
        foreach (var fixture in new[] { ("long", 900, 12000), ("wide", 1800, 1000), ("small", 300, 180), ("flat", 6000, 400) })
        {
            using Bitmap source = new(fixture.Item2, fixture.Item3);
            using (Graphics g = Graphics.FromImage(source))
            {
                g.Clear(Color.White);
                g.FillRectangle(Brushes.CornflowerBlue, 0, source.Height / 3, source.Width, Math.Max(1, source.Height / 3));
            }
            using RegionCaptureForm editor = ScreenshotEditor.Create(source, CapturePalette.Dark);
            editor.Show(main);
            try
            {
                await Task.Delay(250);
                object manager = Property(editor, "ShapeManager");
                Array nodes = (Array)Property(manager, "ResizeNodes");
                float Zoom() => (float)Property(editor, "ZoomFactor");
                RectangleF Display(RectangleF rectangle) => new(rectangle.X * Zoom(), rectangle.Y * Zoom(), rectangle.Width * Zoom(), rectangle.Height * Zoom());
                void CheckViewport(string stage)
                {
                    int top = Field<int>(editor, "ToolbarHeight");
                    RectangleF viewport = new(0, top, editor.ClientSize.Width, editor.ClientSize.Height - top);
                    RectangleF crop = Display(Crop(manager).Rectangle);
                    Console.WriteLine($"{fixture.Item1}/{stage}: DPI={editor.DeviceDpi}, viewport={viewport}, crop={crop}, zoom={Zoom()}");
                    Check(viewport.Contains(crop), "complete crop boundary fits below the toolbar: " + fixture.Item1 + "/" + stage);
                    Check(nodes.Cast<object>().Take(8).Where(node => (bool)Property(node, "Visible"))
                        .All(node => viewport.Contains(Display((RectangleF)Property(node, "Rectangle")))),
                        "all visible crop hit targets fit entirely inside the viewport: " + fixture.Item1 + "/" + stage);
                    Check((bool)Property(nodes.GetValue(1)!, "Visible") && (bool)Property(nodes.GetValue(5)!, "Visible"),
                        "top and bottom handles remain available: " + fixture.Item1 + "/" + stage);
                    var objects = ((System.Collections.IEnumerable)Property(manager, "DrawableObjects")).Cast<object>();
                    Check(!objects.Where(item => item.GetType().Name == "ImageEditorScrollbar").Any(item => (bool)Property(item, "Visible")),
                        "fitted image has no overlapping scrollbars: " + fixture.Item1 + "/" + stage);
                }
                EditorRegression.Capture(editor, "crop-viewport-" + fixture.Item1);
                CheckViewport("open");
                RectangleF full = editor.CanvasRectangle;
                int delta = Math.Max(2, (int)Math.Ceiling(18 * editor.DeviceDpi / (96f * Zoom())));
                await DragAsync(editor, manager, 1, 0, delta);
                Check(Crop(manager).Rectangle.Top > full.Top + 1 && Math.Abs(Crop(manager).Rectangle.Left - full.Left) < 1,
                    "top handle changes height without changing width: " + fixture.Item1);
                await DragAsync(editor, manager, 5, 0, -delta);
                Check(Crop(manager).Rectangle.Bottom < full.Bottom - 1 && Math.Abs(Crop(manager).Rectangle.Right - full.Right) < 1,
                    "bottom handle changes height without changing width: " + fixture.Item1);
                RectangleF relative = new(Crop(manager).Rectangle.X - full.X, Crop(manager).Rectangle.Y - full.Y,
                    Crop(manager).Rectangle.Width, Crop(manager).Rectangle.Height);
                editor.Size = editor.MinimumSize;
                await Task.Delay(180);
                CheckViewport("minimum");
                RectangleF after = Crop(manager).Rectangle;
                Check(Math.Abs(after.Top - editor.CanvasRectangle.Top - relative.Top) < 1 && Math.Abs(after.Height - relative.Height) < 1,
                    "resizing the editor preserves selected image pixels: " + fixture.Item1);
                EditorRegression.Capture(editor, "crop-viewport-" + fixture.Item1 + "-minimum");
                editor.GetType().GetProperty("ZoomFactor", Members)!.SetValue(editor, 6f);
                Toolbar(editor).Items["editorCrop"]!.PerformClick();
                await Task.Delay(80);
                CheckViewport("recover-zoom");
                Check(Math.Abs(Crop(manager).Rectangle.Top - editor.CanvasRectangle.Top - relative.Top) < 1 &&
                    Math.Abs(Crop(manager).Rectangle.Height - relative.Height) < 1,
                    "reselecting crop recovers the frame without changing selected pixels: " + fixture.Item1);
                Check(editor.ConfirmEditorCrop(), "native crop action accepts adjusted height: " + fixture.Item1);
                using Bitmap output = editor.GetResultImage();
                Check(output.Width == source.Width && output.Height < source.Height,
                    "confirmed image has the cropped height and original width: " + fixture.Item1);
            }
            finally { Call(editor, "CloseWindow", RegionResult.Close); }
        }
    }

    internal static async Task RunAsync(MainForm main, bool simulate = false)
    {
        simulateInput = simulate;
        Console.WriteLine(simulate ? "Input mode: isolated crop-handle events (no desktop pointer input)" : "Input mode: real desktop pointer");
        Bitmap source = new(600, 400);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++) source.SetPixel(x, y, Color.FromArgb(x % 256, y % 256, (x + y) % 256));
        using Bitmap original = (Bitmap)source.Clone();
        Call(main, "SetPreview", source);
        Rectangle appliedArea = Rectangle.Empty;
        Point previousCursor = Cursor.Position;
        try
        {
            await EditorRegression.OpenAsync(main, async editor =>
            {
                if (!simulateInput) { editor.TopMost = true; editor.ForceActivate(); }
                editor.Options.InputDelay = 0;
                await Task.Delay(240);
                object manager = Property(editor, "ShapeManager");
                Console.WriteLine($"initial crop={Crop(manager).Rectangle}; canvas={editor.CanvasRectangle}");
                Check(editor.Options.ImageEditorCropFromEdges && SameArea(Crop(manager).Rectangle, editor.CanvasRectangle),
                    "crop opens at the full image boundaries without drawing a selection");
                Array nodes = (Array)Property(manager, "ResizeNodes");
                float zoom = (float)Property(editor, "ZoomFactor");
                Check(nodes.Cast<object>().Take(8).All(node => (bool)Property(node, "Visible") &&
                    (int)Property(node, "Size") * zoom >= 12 * editor.DeviceDpi / 96f),
                    "all eight edge and corner handles are visible at a usable screen size");
                RectangleF full = editor.CanvasRectangle;
                await DragAsync(editor, manager, 1, 0, 35);
                Check(Crop(manager).Rectangle.Top > full.Top + 30 && Math.Abs(Crop(manager).Rectangle.Left - full.Left) < 1,
                    "dragging the top handle moves only the top boundary");
                await DragAsync(editor, manager, 7, 45, 0);
                Check(Crop(manager).Rectangle.Left > full.Left + 40, "dragging the left handle moves the left boundary");
                await DragAsync(editor, manager, 4, -50, -30);
                RectangleF adjusted = Crop(manager).Rectangle;
                Check(adjusted.Right < full.Right - 40 && adjusted.Bottom < full.Bottom - 20,
                    "dragging a corner adjusts two image boundaries together");
                Check(editor.Canvas.Size == original.Size, "dragging boundaries keeps the original pixels until confirmation");
                EditorRegression.Capture(editor, "crop-edges-adjusted");
                Rectangle expected = Rectangle.Round(new RectangleF(adjusted.X - full.X, adjusted.Y - full.Y, adjusted.Width, adjusted.Height));
                Call(editor, "RegionCaptureForm_KeyDown", editor, new KeyEventArgs(Keys.Enter));
                Check(editor.Visible && editor.DialogResult == DialogResult.None, "Enter confirms crop and keeps the editor open");
                using (Bitmap result = editor.GetResultImage()) MatchPixels(original, result, expected);
                object history = Field<object>(manager, "history");
                Call(history, "Undo");
                using (Bitmap undone = editor.GetResultImage()) Check(ImageHelpers.CompareImages(original, undone), "undo restores all original image boundaries and pixels");
                Call(history, "Redo");
                using (Bitmap redone = editor.GetResultImage()) MatchPixels(original, redone, expected);
                Call(history, "Undo");

                await StartCropAsync(editor);
                Check(SameArea(Crop(manager).Rectangle, editor.CanvasRectangle), "starting crop again creates the current full image boundary");
                await DragAsync(editor, manager, 3, -60, 0);
                Call(editor, "RegionCaptureForm_KeyDown", editor, new KeyEventArgs(Keys.Escape));
                using (Bitmap cancelled = editor.GetResultImage())
                    Check(editor.Visible && ImageHelpers.CompareImages(original, cancelled), "Escape cancels boundary changes without leaving the editor");

                await StartCropAsync(editor);
                full = editor.CanvasRectangle;
                await DragAsync(editor, manager, 5, 0, -70);
                adjusted = Crop(manager).Rectangle;
                appliedArea = Rectangle.Round(new RectangleF(adjusted.X - full.X, adjusted.Y - full.Y, adjusted.Width, adjusted.Height));
                string exportPath = Path.GetFullPath("artifacts/image-editor/boundary-export.png");
                editor.ImageFilePath = exportPath;
                Call(editor, "OnSaveImageRequested");
                using (Bitmap exported = new(exportPath)) MatchPixels(original, exported, appliedArea);
                Check(editor.Visible && editor.Canvas.Size == appliedArea.Size, "saving confirms pending image boundaries before exporting pixels");
                Call(history, "Undo");
                await StartCropAsync(editor);
                full = editor.CanvasRectangle;
                await DragAsync(editor, manager, 5, 0, -70);
                adjusted = Crop(manager).Rectangle;
                appliedArea = Rectangle.Round(new RectangleF(adjusted.X - full.X, adjusted.Y - full.Y, adjusted.Width, adjusted.Height));
                EditorRegression.Actions(editor).Items["editorApply"]!.PerformClick();
            });
            MatchPixels(original, Field<Bitmap>(main, "result"), appliedArea);
            Check(ReferenceEquals(Field<Bitmap>(main, "result"), Field<PictureBox>(main, "preview").Image),
                "apply confirms pending crop boundaries and updates the main preview");
        }
        finally { if (!simulateInput) { InputHelpers.SendMouseUp(); InputHelpers.SendMouseMove(previousCursor); } }
    }
}
