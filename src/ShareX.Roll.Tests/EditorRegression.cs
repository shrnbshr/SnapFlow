using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.Reflection;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static ShareX.Roll.Localization.UiText;

internal static class EditorRegression
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string Output = Path.GetFullPath("artifacts/image-editor");
    private delegate bool WindowCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, WindowCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Members)!.GetValue(target)!;
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Members)!.Invoke(target, args);
    private static object Property(object target, string name) => target.GetType().GetProperty(name, Members)!.GetValue(target)!;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    internal static ToolStrip Tools(RegionCaptureForm editor) => editor.Controls.Find("editorToolbar", true).OfType<ToolStrip>().Single();
    internal static ToolStrip Actions(RegionCaptureForm editor) => editor.Controls.Find("editorActions", true).OfType<ToolStrip>().Single();
    internal static void Capture(Form form, string name)
    {
        using Bitmap image = new(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        using (Graphics g = Graphics.FromImage(image))
            foreach (Form child in form.OwnedForms.Where(child => child.Visible))
            {
                using Bitmap menu = new(child.Width, child.Height);
                child.DrawToBitmap(menu, new Rectangle(Point.Empty, child.Size));
                g.DrawImageUnscaled(menu, child.Left - form.Left, child.Top - form.Top);
            }
        image.Save(Path.Combine(Output, name + "-" + form.DeviceDpi + ".png"), ImageFormat.Png);
    }

    internal static async Task OpenAsync(MainForm main, Func<RegionCaptureForm, Task> inspect)
    {
        Task driver = DriveAsync();
        main.Controls.Find("editButton", true).OfType<Button>().Single().PerformClick();
        await driver;

        async Task DriveAsync()
        {
            // The native toolbar pumps messages during its first handle/font creation.
            // Wait for completed chrome and initial crop rather than inspecting a half-created window.
            RegionCaptureForm? editor = null;
            Stopwatch startup = Stopwatch.StartNew();
            while (startup.Elapsed.TotalSeconds < 8)
            {
                await Task.Delay(30);
                editor = Field<RegionCaptureForm>(main, "editorForm");
                if (editor is { Visible: true, IsEditorCropPending: true } &&
                    editor.Controls.Find("editorToolbar", true).OfType<ToolStrip>()
                        .Any(strip => strip.Top > 0 && strip.Left > 0 && strip.Right <= editor.ClientSize.Width)) break;
            }
            if (editor == null || !editor.Visible || !editor.IsEditorCropPending)
                throw new InvalidOperationException("Editor did not finish opening: " + main.Controls.Find("statusLabel", true).FirstOrDefault()?.Text);
            Check(editor.Visible, "edit action opens a separate ShareX editor window");
            try { await inspect(editor); }
            finally { if (!editor.IsDisposed && editor.Visible && editor.DialogResult == DialogResult.None) Call(editor, "CloseWindow", RegionResult.Close); }
        }
    }

    internal static async Task RunAsync(MainForm main)
    {
        Directory.CreateDirectory(Output);
        Button edit = main.Controls.Find("editButton", true).OfType<Button>().Single();
        Check(!edit.Enabled, "edit is unavailable before a screenshot");
        Bitmap original = new(600, 400);
        for (int y = 0; y < original.Height; y++)
            for (int x = 0; x < original.Width; x++)
                original.SetPixel(x, y, Color.FromArgb(x % 256, y % 256, (x + y) % 256));
        Call(main, "SetPreview", original);
        Check(edit.Enabled && edit.Text == L("Edit"), "preview enables the localized edit button");
        Size previousSize = main.Size;
        main.Size = main.MinimumSize;
        await Task.Delay(160);
        Control toolbar = main.Controls.Find("previewToolbar", true).Single();
        Capture(main, "main-minimum");
        Console.WriteLine($"DPI={main.DeviceDpi}; main={main.Size}; toolbar={toolbar.ClientRectangle}; actions={edit.Parent!.Bounds}; copy={Field<Button>(main, "copy").Bounds}; edit={edit.Bounds}");
        foreach (Button action in new[] { Field<Button>(main, "copy"), Field<Button>(main, "save"), edit })
            Check(toolbar.RectangleToScreen(toolbar.ClientRectangle).Contains(action.RectangleToScreen(action.ClientRectangle)),
                action.Text + " fits in the minimum-width preview toolbar");
        Capture(main, "main-minimum");
        main.Size = previousSize;

        await OpenAsync(main, editor =>
        {
            Check(!ReferenceEquals(editor.Canvas, original), "editor owns a copy of the preview");
            Check(Field<List<HotkeyInfo>>(main, "hotkeys").Count == 0, "global capture shortcuts are released while editing");
            ToolStrip strip = Tools(editor);
            Check(Actions(editor).Items["editorApply"]!.Text == L("ApplyPreview") && Actions(editor).Items["editorSaveAs"]!.Text == L("ExportImage"),
                "toolbar clearly labels apply and export");
            Check(strip.Items["editorUpload"] == null && strip.Items["editorPrint"] == null, "unsupported actions are absent");
            Console.WriteLine($"tools={strip.Bounds}; crop={strip.Items["editorCrop"]!.Bounds}; placement={strip.Items["editorCrop"]!.Placement}; owner={strip.Parent!.Bounds}");
            Check(strip.Items["editorCrop"]!.Placement == ToolStripItemPlacement.Main, "crop is labeled and visible without opening overflow");
            Check((ShapeType)Property(Property(editor, "ShapeManager"), "CurrentTool") == ShapeType.ToolCrop, "crop is selected on opening");
            VerifyAnnotationProfile(editor, strip);
            Capture(editor, "editor-open");
            Call(editor, "CloseWindow", RegionResult.Close);
            return Task.CompletedTask;
        });
        Check(ReferenceEquals(Field<Bitmap>(main, "result"), original) && original.GetPixel(123, 89).R == 123,
            "closing the editor preserves the original bitmap and preview");
        Check(Field<List<HotkeyInfo>>(main, "hotkeys").Count > 0, "global capture shortcuts are restored");

        Rectangle crop = new(71, 53, 250, 170);
        await OpenAsync(main, async editor =>
        {
            object manager = Property(editor, "ShapeManager");
            RectangleF area = new(editor.CanvasRectangle.X + crop.X, editor.CanvasRectangle.Y + crop.Y, crop.Width, crop.Height);
            Call(manager, "CropArea", area);
            Check(editor.Canvas.Size == crop.Size, "upstream crop produces the requested pixel size");
            object history = Field<object>(manager, "history");
            Call(history, "Undo");
            Check(editor.Canvas.Size == original.Size, "upstream undo restores the complete image");
            Call(history, "Redo");
            Check(editor.Canvas.Size == crop.Size, "upstream redo restores the crop");
            using (Bitmap result = editor.GetResultImage())
            {
                for (int y = 0; y < result.Height; y++)
                    for (int x = 0; x < result.Width; x++)
                        if (result.GetPixel(x, y) != original.GetPixel(crop.X + x, crop.Y + y))
                            throw new InvalidOperationException("Crop pixel mismatch");
                Check(true, "every cropped pixel matches its position in the original");
                foreach ((string ext, ImageFormat format) in new[] { ("png", ImageFormat.Png), ("jpg", ImageFormat.Jpeg), ("gif", ImageFormat.Gif), ("bmp", ImageFormat.Bmp), ("tiff", ImageFormat.Tiff) })
                {
                    string path = Path.Combine(Output, "export." + ext);
                    editor.ImageFilePath = path;
                    Call(editor, "OnSaveImageRequested");
                    Check(File.Exists(path), "editor save callback writes " + ext);
                    using Bitmap loaded = new(path);
                    Check(loaded.Size == crop.Size && loaded.RawFormat.Guid == format.Guid, ext + " has the actual requested encoding and dimensions");
                    if (ext is "png" or "bmp" or "tiff")
                        Check(ImageHelpers.CompareImages(result, loaded), ext + " retains every pixel");
                }
            }
            string lastPath = editor.ImageFilePath;
            uint uiThread = GetWindowThreadProcessId(editor.Handle, out _);
            Task cancelExport = Task.Run(async () =>
            {
                Stopwatch deadline = Stopwatch.StartNew();
                while (deadline.Elapsed.TotalSeconds < 10)
                {
                    IntPtr dialog = IntPtr.Zero;
                    EnumThreadWindows(uiThread, (window, _) =>
                    {
                        StringBuilder className = new(256);
                        GetClassName(window, className, className.Capacity);
                        if (className.ToString() == "#32770") { dialog = window; return false; }
                        return true;
                    }, IntPtr.Zero);
                    if (dialog != IntPtr.Zero)
                    {
                        PostMessage(dialog, 0x10, IntPtr.Zero, IntPtr.Zero);
                        return;
                    }
                    await Task.Delay(80);
                }
                throw new TimeoutException("Export dialog did not appear");
            });
            ToolStrip exportStrip = Tools(editor);
            Actions(editor).Items["editorSaveAs"]!.PerformClick();
            await cancelExport;
            Check(editor.Visible && editor.Canvas.Size == crop.Size && editor.ImageFilePath == lastPath,
                "canceling the actual export dialog preserves the image, file path and editor window");
            await Task.Delay(150);
            Capture(editor, "editor-cropped");
            ToolStrip strip = Tools(editor);
            Actions(editor).Items["editorApply"]!.PerformClick();
        });
        Bitmap applied = Field<Bitmap>(main, "result");
        Check(applied.Size == crop.Size && ReferenceEquals(applied, Field<PictureBox>(main, "preview").Image),
            "apply updates the main preview with the cropped image");
        Check(applied.GetPixel(10, 10) == Color.FromArgb(81, 63, 144), "applied preview retains original crop coordinates");
        Capture(main, "main-applied");

        await OpenAsync(main, editor =>
        {
            Call(Property(editor, "ShapeManager"), "CropArea", new RectangleF(editor.CanvasRectangle.X, editor.CanvasRectangle.Y, 40, 30));
            Call(editor, "CloseWindow", RegionResult.Close);
            return Task.CompletedTask;
        });
        Check(ReferenceEquals(Field<Bitmap>(main, "result"), applied) && applied.Size == crop.Size,
            "discarding further edits leaves the applied image unchanged");

        using Bitmap tall = new(240, 6000);
        using (Graphics g = Graphics.FromImage(tall)) g.Clear(Color.CornflowerBlue);
        CapturePalette.Dark.ApplyLibraryTheme();
        using RegionCaptureForm tallEditor = ScreenshotEditor.Create(tall, CapturePalette.Dark);
        tallEditor.Show(main);
        await Task.Delay(220);
        Check((float)Property(tallEditor, "ZoomFactor") < 1, "long scrolling screenshots open fitted to the editor");
        Capture(tallEditor, "editor-tall-dark");
        Call(tallEditor, "CloseWindow", RegionResult.Close);
        Field<CapturePalette>(main, "appearancePalette").ApplyLibraryTheme();
        Check(applied.Width == crop.Width, "closing other editor instances does not dispose the main result");
    }

    private static void VerifyAnnotationProfile(RegionCaptureForm editor, ToolStrip strip)
    {
        Check(editor.Options.LimitImageEditorToAnnotations &&
            strip.Items["editorImageMenu"] == null && strip.Items["editorOptionsMenu"] == null,
            "whole-image operations, image effects, advanced options and help have no menu entry");
        HashSet<ShapeType> expected = new()
        {
            ShapeType.ToolSelect, ShapeType.ToolCrop,
            ShapeType.DrawingRectangle, ShapeType.DrawingEllipse, ShapeType.DrawingFreehand,
            ShapeType.DrawingFreehandArrow, ShapeType.DrawingLine, ShapeType.DrawingArrow,
            ShapeType.DrawingTextOutline, ShapeType.DrawingTextBackground, ShapeType.DrawingSpeechBalloon,
            ShapeType.DrawingStep, ShapeType.DrawingMagnify, ShapeType.DrawingImage,
            ShapeType.DrawingImageScreen, ShapeType.DrawingSticker, ShapeType.DrawingCursor,
            ShapeType.DrawingSmartEraser, ShapeType.EffectBlur, ShapeType.EffectPixelate,
            ShapeType.EffectHighlight, ShapeType.ToolSpotlight
        };
        Check(expected.SetEquals(strip.Items.Cast<ToolStripItem>().Where(item => item.Tag is ShapeType).Select(item => (ShapeType)item.Tag!)),
            "toolbar contains manual crop and all annotation tools, with no cut-out or capture tools");
        object manager = Property(editor, "ShapeManager");
        Call(manager, "form_KeyDown", editor, new KeyEventArgs(Keys.X));
        Check((ShapeType)Property(manager, "CurrentTool") == ShapeType.ToolCrop,
            "removed cut-out tool cannot be reactivated by its X shortcut");
        editor.Options.InputDelay = 0;
        Call(editor, "RegionCaptureForm_KeyDown", editor, new KeyEventArgs(Keys.Space));
        Call(editor, "RegionCaptureForm_KeyDown", editor, new KeyEventArgs(Keys.Oemtilde));
        Check(editor.Visible && editor.DialogResult == DialogResult.None,
            "capture-only Space and tilde shortcuts cannot close the annotation editor");
        Check(editor.Options.RegionCaptureActionX1Click == RegionCaptureAction.None &&
            editor.Options.RegionCaptureActionX2Click == RegionCaptureAction.None,
            "mouse side buttons cannot invoke removed capture actions");

        using Bitmap before = editor.GetResultImage();
        manager.GetType().GetProperty("CurrentTool")!.SetValue(manager, ShapeType.DrawingRectangle);
        RectangleDrawingShape rectangle = (RectangleDrawingShape)manager.GetType().GetMethod("CreateShape", Members,
            null, new[] { typeof(ShapeType) }, null)!.Invoke(manager, new object[] { ShapeType.DrawingRectangle })!;
        rectangle.Rectangle = new RectangleF(editor.CanvasRectangle.X + 80, editor.CanvasRectangle.Y + 70, 120, 80);
        rectangle.BorderColor = rectangle.FillColor = Color.Red;
        rectangle.Shadow = false;
        rectangle.OnCreated();
        manager.GetType().GetMethod("AddShape", Members, null, new[] { typeof(BaseShape) }, null)!.Invoke(manager, new object[] { rectangle });
        using (Bitmap annotated = editor.GetResultImage())
            Check(annotated.Size == before.Size && annotated.GetPixel(110, 90).ToArgb() == Color.Red.ToArgb(),
                "annotation styles render onto the image without resizing it");
        ToolStripDropDownItem editMenu = (ToolStripDropDownItem)strip.Items["editorEditMenu"]!;
        editMenu.DropDownItems[0].PerformClick();
        using (Bitmap undone = editor.GetResultImage())
            Check(ImageHelpers.CompareImages(before, undone), "retained edit-menu undo restores every original pixel");
        editMenu.DropDownItems[1].PerformClick();
        using (Bitmap redone = editor.GetResultImage())
            Check(redone.GetPixel(110, 90).ToArgb() == Color.Red.ToArgb(), "retained edit-menu redo restores the annotation");
        Call(manager, "DeleteAllShapes", false);
        manager.GetType().GetProperty("CurrentTool")!.SetValue(manager, ShapeType.ToolCrop);
    }
}
