using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Reflection;
using System.IO;

internal static class EditorChromeRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) { Console.WriteLine("FAIL: " + message); throw new InvalidOperationException(message); }
        Console.WriteLine("PASS: " + message);
    }
    internal static async Task RunAsync(MainForm main)
    {
        Directory.CreateDirectory("artifacts/image-editor");
        bool motion = UiMotion.UserEnabled;
        try
        {
            foreach (CapturePalette palette in new[] { CapturePalette.Light, CapturePalette.Dark })
            {
                palette.ApplyLibraryTheme();
                using Bitmap image = new(600, 360);
                using (Graphics g = Graphics.FromImage(image)) { g.Clear(Color.White); g.FillRectangle(Brushes.CornflowerBlue, 40, 40, 520, 200); }
                using RegionCaptureForm editor = ScreenshotEditor.Create(image, palette);
                editor.Show(main);
                await Task.Delay(500);
                ToolStrip tools = editor.OwnedForms.SelectMany(f => f.Controls.OfType<ToolStrip>()).Single();
                ToolStrip actions = EditorRegression.Actions(editor);
                Check(tools.Top >= actions.Bottom && tools.Height >= 40 * editor.DeviceDpi / 96,
                    "annotation tools occupy a separate generous row");
                Check(actions.Items["editorApply"]!.Placement == ToolStripItemPlacement.Main,
                    "apply remains visible in the output row");
                Check(tools.Items["editorConfirmCrop"]!.Visible && editor.IsEditorCropPending,
                    "crop exposes keyboard-accessible native confirmation");
                long PaintCount() => (long)editor.GetType().GetField("editorPaintCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
                float zoom = (float)editor.GetType().GetProperty("ZoomFactor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
                int toolbarHeight = (int)editor.GetType().GetField("ToolbarHeight", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
                RectangleF display = new(editor.CanvasRectangle.X * zoom, editor.CanvasRectangle.Y * zoom,
                    editor.CanvasRectangle.Width * zoom, editor.CanvasRectangle.Height * zoom);
                Console.WriteLine($"DPI={editor.DeviceDpi}; client={editor.ClientArea}; toolbar={toolbarHeight}; menu={tools.Parent!.Bounds}; display={display}; zoom={zoom}");
                Check(display.Top >= toolbarHeight && display.Bottom <= editor.ClientArea.Bottom,
                    "fitted image stays entirely below the toolbar and inside the viewport");
                long baseline = PaintCount();
                Point pointerBefore = Control.MousePosition;
                await Task.Delay(350);
                long paints = PaintCount() - baseline;
                Console.WriteLine($"idle diagnostics: pointer={pointerBefore} -> {Control.MousePosition}; buttons={Control.MouseButtons}; moves={editor.GetType().GetField("editorPointerMoveCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)}; clock={((System.Windows.Forms.Timer)editor.GetType().GetField("editorInputTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!).Enabled}");
                Check(paints <= 2, $"idle editor stops full-image repainting ({paints} frames / 350ms)");
                baseline = PaintCount();
                typeof(ToolStripItem).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(actions.Items["editorCopy"], [EventArgs.Empty]);
                await Task.Delay(220);
                paints = PaintCount() - baseline;
                Check(paints <= 2, $"toolbar feedback does not repaint the image ({paints} frames)");
                EditorRegression.Capture(editor, palette == CapturePalette.Light ? "chrome-light" : "chrome-dark");
                tools.Items["editorCancelCrop"]!.PerformClick();
                Check(!editor.IsEditorCropPending && editor.Canvas.Size == image.Size, "cancel crop preserves image bounds");
                tools.Items["editorCrop"]!.PerformClick();
                tools.Items["editorConfirmCrop"]!.PerformClick();
                Check(!editor.IsEditorCropPending && editor.Canvas.Size == image.Size, "confirm full-frame crop preserves all pixels");
                editor.Width = (int)(660 * editor.DeviceDpi / 96f);
                await Task.Delay(150);
                Check(actions.Items["editorApply"]!.Placement == ToolStripItemPlacement.Main,
                    "compact layout preserves the primary output action");
                Check(tools.OverflowButton.Visible, "compact layout makes additional annotation tools available in overflow");
                await CheckOverflowAsync(editor, tools, palette);
                EditorRegression.Capture(editor, palette == CapturePalette.Light ? "chrome-compact-light" : "chrome-compact-dark");
                UiMotion.UserEnabled = false;
                typeof(ToolStripItem).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(actions.Items["editorCopy"], [EventArgs.Empty]);
                Check(!UiMotion.Enabled, "reduced-motion preference disables optional editor motion");
                UiMotion.UserEnabled = motion;
                editor.GetType().GetMethod("CloseWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [RegionResult.Close]);
            }
        }
        finally { UiMotion.UserEnabled = motion; CapturePalette.Current.ApplyLibraryTheme(); }
    }

    private static async Task CheckOverflowAsync(RegionCaptureForm editor, ToolStrip tools, CapturePalette palette)
    {
        float dpi = editor.DeviceDpi / 96f;
        var original = tools.Items.Cast<ToolStripItem>().ToDictionary(item => item, item => (item.Size, item.DisplayStyle, item.Text));
        ToolStripOverflowButton more = tools.OverflowButton;
        Console.WriteLine($"overflow button: size={more.Size}; preferred={more.GetPreferredSize(Size.Empty)}; padding={more.Padding}; label={more.AccessibleName}");
        Check(more.Width >= 32 * dpi && more.Height >= 32 * dpi && more.AccessibleName == ShareX.Roll.Localization.UiText.L("EditorMoreTools"),
            "more tools has a comfortable labeled target instead of a narrow overflow arrow");
        more.ShowDropDown();
        await Task.Delay(100);
        ToolStripDropDown menu = more.DropDown;
        ToolStripItem[] items = tools.Items.Cast<ToolStripItem>().Where(item => item.Available && item.GetCurrentParent() == menu).ToArray();
        Console.WriteLine($"overflow opened: visible={menu.Visible}; bounds={menu.Bounds}; items={items.Length}");
        foreach (ToolStripItem item in items) Console.WriteLine($"overflow item: {item.Text}; {item.Bounds}; {item.DisplayStyle}");
        Check(menu.Visible && items.Length > 0 && items.All(item => item.DisplayStyle == ToolStripItemDisplayStyle.ImageAndText && !string.IsNullOrWhiteSpace(item.Text)),
            "overflow presents every command with an icon and a readable label");
        Check(items.Select(item => item.Bounds.Left).Distinct().Count() == 1 &&
            items.Zip(items.Skip(1), (a, b) => a.Bounds.Bottom <= b.Bounds.Top).All(value => value),
            "overflow tools form a single aligned column without a wrapping icon grid");
        Check(Screen.FromControl(editor).WorkingArea.Contains(menu.Bounds), "overflow menu stays inside the available screen");
        ToolStripItem? next = menu.GetNextItem(items[0], ArrowDirection.Down);
        Check(next == items.ElementAtOrDefault(1), "native downward keyboard navigation follows the visible menu order");
        using (Bitmap popup = new(menu.Width, menu.Height))
        {
            menu.DrawToBitmap(popup, new Rectangle(Point.Empty, menu.Size));
            Check(popup.GetPixel((int)(3 * dpi), menu.Height / 2).ToArgb() == palette.Surface.ToArgb(),
                "overflow background follows the editor appearance");
            string theme = palette == CapturePalette.Light ? "light" : "dark";
            EditorRegression.Capture(editor, "overflow-context-" + theme);
            using Bitmap context = new(Path.Combine("artifacts/image-editor", $"overflow-context-{theme}-{editor.DeviceDpi}.png"));
            using (Graphics g = Graphics.FromImage(context)) g.DrawImageUnscaled(popup, menu.Left - editor.Left, menu.Top - editor.Top);
            context.Save(Path.Combine("artifacts/image-editor", $"overflow-open-{theme}-{editor.DeviceDpi}.png"));
            popup.Save(Path.Combine("artifacts/image-editor", $"overflow-menu-{theme}-{editor.DeviceDpi}.png"));
        }
        ToolStripDropDownItem editMenu = (ToolStripDropDownItem)items.Single(item => item.Name == "editorEditMenu");
        editMenu.ShowDropDown();
        await Task.Delay(50);
        Check(editMenu.DropDown.Visible && editMenu.DropDownItems.Count > 0 && Screen.FromControl(editor).WorkingArea.Contains(editMenu.DropDown.Bounds),
            "native edit submenu remains reachable and stays on screen");
        editMenu.HideDropDown();
        ToolStripItem pixelate = items.Single(item => Equals(item.Tag, ShapeType.EffectPixelate));
        pixelate.PerformClick();
        menu.Close();
        await Task.Delay(50);
        object manager = editor.GetType().GetProperty("ShapeManager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
        Check((ShapeType)manager.GetType().GetProperty("CurrentTool")!.GetValue(manager)! == ShapeType.EffectPixelate,
            "overflow invokes the original pixelate command");
        Check(original.All(pair => pair.Key.Size == pair.Value.Size && pair.Key.DisplayStyle == pair.Value.DisplayStyle && pair.Key.Text == pair.Value.Text),
            "closing overflow restores toolbar sizing and display styles");
        more.ShowDropDown();
        await Task.Delay(30);
        editor.Width += (int)(20 * dpi);
        await Task.Delay(30);
        Check(!menu.Visible, "resizing dismisses overflow and restores normal toolbar layout");
    }
}
