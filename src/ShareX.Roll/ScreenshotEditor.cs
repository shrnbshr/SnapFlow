// GPL-3.0-or-later. Reuse the bundled ShareX editor, crop/history engine and image encoders.
using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using Microsoft.Win32;
using static ShareX.Roll.Localization.UiText;

namespace ShareX.Roll;

internal static class ScreenshotEditor
{
    internal static RegionCaptureForm Create(Bitmap source, CapturePalette palette, ApplicationPreferences? preferences = null)
    {
        RegionCaptureOptions options = new()
        {
            ImageEditorStartMode = ImageEditorStartMode.Normal,
            LimitImageEditorToAnnotations = true,
            ImageEditorCropFromEdges = true,
            ZoomToFitOnOpen = true,
            LastEditorTool = ShapeType.ToolCrop,
            EditorAutoCopyImage = false,
            AutoCloseEditorOnTask = false,
            ShowMagnifier = false,
            ShowInfo = false,
            ShowEditorPanTip = false,
            RegionCaptureActionX1Click = RegionCaptureAction.None,
            RegionCaptureActionX2Click = RegionCaptureAction.None,
            EnableAnimations = false,
            UseEditorChrome = true
        };
        // The editor owns this clone; closing or cropping can never dispose the main preview.
        Bitmap canvas = (Bitmap)source.Clone();
        RegionCaptureForm editor;
        try { editor = new RegionCaptureForm(RegionCaptureMode.Editor, options, canvas); }
        catch { canvas.Dispose(); throw; }
        editor.Name = "screenshotEditor";
        editor.Font = new Font("Microsoft YaHei UI", 10);
        WindowBackdrop backdrop = new(editor, () =>
        {
            Rectangle imageBounds = Rectangle.Ceiling(editor.EditorImageBounds);
            // Crop handles and the image edge remain opaque along with the image itself.
            imageBounds.Inflate((int)Math.Ceiling(8 * editor.DeviceDpi / 96f), (int)Math.Ceiling(8 * editor.DeviceDpi / 96f));
            return imageBounds;
        });
        bool transparency = preferences?.TransparencyEnabled ?? true;
        float scale = editor.DeviceDpi / 96f;
        Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        editor.Size = new Size(Math.Min(workingArea.Width, (int)(1000 * scale)), Math.Min(workingArea.Height, (int)(720 * scale)));
        editor.Location = new Point(workingArea.Left + (workingArea.Width - editor.Width) / 2,
            workingArea.Top + (workingArea.Height - editor.Height) / 2);
        void UpdateTitle()
        {
            if (editor.Text.StartsWith("ShareX - ", StringComparison.Ordinal)) editor.Text = "SnapFlow - " + editor.Text[9..];
        }
        editor.TextChanged += (_, _) => UpdateTitle();
        UpdateTitle();
        editor.SaveImageRequested += (image, path) => Save(editor, image, path, false, preferences);
        editor.SaveImageAsRequested += (image, path) => Save(editor, image, path, true, preferences);
        editor.CopyImageRequested += image => ClipboardHelpers.CopyImage(image);
        List<ToolStripItem> removedItems = new();
        // ShareX retains references to menu controls for state updates; dispose them with the editor.
        editor.Disposed += (_, _) => { foreach (ToolStripItem item in removedItems) item.Dispose(); };
        editor.EditorToolbarCreated += toolbar =>
        {
            toolbar.Name = "editorToolbar";
            // Keep output actions, manual crop, annotations, their styles and object editing.
            foreach (string name in new[] { "editorUpload", "editorPrint", "editorImageMenu", "editorOptionsMenu" })
            {
                ToolStripItem item = toolbar.Items[name]!;
                toolbar.Items.Remove(item);
                removedItems.Add(item);
            }
            ToolStripItem cutOut = toolbar.Items.Cast<ToolStripItem>().Single(item => Equals(item.Tag, ShapeType.ToolCutOut));
            toolbar.Items.Remove(cutOut);
            removedItems.Add(cutOut);
            for (int i = toolbar.Items.Count - 1; i >= 0; i--)
            {
                if (toolbar.Items[i] is ToolStripSeparator separator &&
                    (i == 0 || i == toolbar.Items.Count - 1 || toolbar.Items[i - 1] is ToolStripSeparator))
                {
                    toolbar.Items.RemoveAt(i);
                    removedItems.Add(separator);
                }
            }
            LabelAction(toolbar.Items["editorApply"]!, L("ApplyPreview"));
            LabelAction(toolbar.Items["editorSaveAs"]!, L("ExportImage"));
            LabelAction(toolbar.Items["editorCopy"]!, L("Copy"));
            LabelAction(toolbar.Items["editorSave"]!, L("Save"));
            ToolStripItem crop = toolbar.Items.Cast<ToolStripItem>().Single(item => Equals(item.Tag, ShapeType.ToolCrop));
            toolbar.Items.Remove(crop);
            toolbar.Items.Insert(toolbar.Items.IndexOf(toolbar.Items["editorCopy"]!) + 1, crop);
            crop.Name = "editorCrop";
            LabelAction(crop, L("CropImage"));
            EditorChrome.Install(editor, toolbar, palette, transparency);
        };
        editor.HandleCreated += (_, _) =>
        {
            NativeMethods.UseImmersiveDarkMode(editor.Handle, palette.Background.GetBrightness() < .5);
            backdrop.Apply(palette, transparency);
        };
        editor.Shown += (_, _) => backdrop.Apply(palette, transparency);
        void RefreshMaterial(object sender, UserPreferenceChangedEventArgs e)
        {
            if (editor.IsDisposed || editor.Disposing || !editor.IsHandleCreated) return;
            try { editor.BeginInvoke((Action)(() => { if (!editor.IsDisposed) backdrop.Apply(palette, transparency); })); }
            catch (InvalidOperationException) { /* The editor closed while the system event was queued. */ }
        }
        SystemEvents.UserPreferenceChanged += RefreshMaterial;
        editor.Disposed += (_, _) => SystemEvents.UserPreferenceChanged -= RefreshMaterial;
        return editor;
    }

    private static void LabelAction(ToolStripItem item, string text)
    {
        item.Text = text;
        item.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        item.AccessibleName = text;
    }

    private static string Save(RegionCaptureForm editor, Bitmap image, string? path, bool saveAs, ApplicationPreferences? preferences)
    {
        // ShareX transfers save-callback bitmap ownership to the host (copy callbacks are borrowed).
        using (image)
        {
            if (!saveAs && !string.IsNullOrEmpty(path))
                return ImageHelpers.SaveImage(image, path) ? path : null!;
            return ScreenshotSaveDialog.Save(image, preferences ?? new(),
                string.IsNullOrEmpty(path) ? LF("ScreenshotFile", DateTime.Now.ToString("yyyyMMdd_HHmmss")) : path,
                owner: editor)!;
        }
    }
}
