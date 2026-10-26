using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Reflection;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Runtime.InteropServices;

internal static class EditorChromeRegression
{
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    private static bool HasRoundedSurface(ToolStripDropDown menu) => menu.Region != null ||
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) &&
        DwmGetWindowAttribute(menu.Handle, 33, out int corners, sizeof(int)) >= 0 && corners is 2 or 3;
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    private static Rectangle PhysicalClient(Control control)
    {
        Point start = control.PointToScreen(Point.Empty), end = control.PointToScreen(new Point(control.ClientSize));
        if (!LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref start) || !LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref end))
            throw new InvalidOperationException("Cannot map editor capture coordinates");
        return Rectangle.FromLTRB(start.X, start.Y, end.X, end.Y);
    }
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
                editor.TopMost = true; // Keep the composed popup captures independent of other desktop windows.
                editor.Show(main);
                await Task.Delay(500);
                ToolStrip tools = EditorRegression.Tools(editor);
                ToolStrip actions = EditorRegression.Actions(editor);
                VerifyIconPadding(tools);
                VerifyIconPadding(actions);
                VerifyTextBalance(tools);
                float scale = editor.DeviceDpi / 96f;
                Control title = editor.Controls.Find("editorTitle", true).Single();
                Check(title.Left == (int)Math.Round(28 * scale) && title.Top == (int)Math.Round(20 * scale) &&
                    tools.Left + tools.Items[0].Bounds.Left + (int)Math.Round(4 * scale) == title.Left,
                    "editor title and tool faces share the workspace's 28px side and 20px top insets");
                Check(actions.Right == editor.ClientSize.Width - (int)Math.Round(24 * scale),
                    "output button faces keep the same 28px trailing inset");
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
                await VerifyFeedbackAsync(actions);
                actions.Focus();
                SendMessage(tools.Handle, 0x127, 2 | (1 << 16), 0); // UIS_CLEAR / UISF_HIDEFOCUS, as native Tab navigation does.
                Message tabFromPreviousControl = Message.Create(actions.Handle, 0x100, (nint)Keys.Tab, 0);
                Application.FilterMessage(ref tabFromPreviousControl);
                tools.Focus();
                Check((bool)tools.Renderer.GetType().GetField("keyboardNavigation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tools.Renderer)!,
                    "first keyboard focus is visible before another arrow key is pressed");
                VerifyBorderlessFeedback(tools, tools.OverflowButton);
                EditorRegression.Capture(editor, palette == CapturePalette.Light ? "chrome-light" : "chrome-dark");
                List<(UiTween Tween, float Value)> hovered = new();
                foreach (ToolStrip strip in new[] { tools, actions })
                {
                    IDictionary feedback = (IDictionary)strip.Renderer.GetType().GetField("states", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(strip.Renderer)!;
                    foreach (ToolStripItem item in strip.Items.Cast<ToolStripItem>().Where(item => item.DisplayStyle is ToolStripItemDisplayStyle.ImageAndText or ToolStripItemDisplayStyle.Text))
                    {
                        object state = feedback[item]!;
                        UiTween tween = (UiTween)state.GetType().GetField("Hover", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
                        hovered.Add((tween, tween.Value));
                        tween.SnapTo(1);
                    }
                }
                try { EditorRegression.Capture(editor, palette == CapturePalette.Light ? "icon-insets-light" : "icon-insets-dark"); }
                finally { foreach ((UiTween tween, float value) in hovered) tween.SnapTo(value); }
                tools.Items["editorCancelCrop"]!.PerformClick();
                Check(!editor.IsEditorCropPending && editor.Canvas.Size == image.Size, "cancel crop preserves image bounds");
                ToolStripDropDownItem colors = (ToolStripDropDownItem)tools.Items["editorAnnotationColor"]!;
                colors.ShowDropDown();
                await Task.Delay(30);
                Check(colors.DropDown.Visible && ReferenceEquals(colors.DropDown.Renderer, tools.Renderer) &&
                    HasRoundedSurface(colors.DropDown) && colors.DropDown.Font.Equals(tools.Font),
                    "annotation palette also shares the rounded editor menu presentation");
                VerifyMenuSpacing(colors.DropDown);
                using (Bitmap popup = new(colors.DropDown.Width, colors.DropDown.Height))
                {
                    colors.DropDown.DrawToBitmap(popup, new Rectangle(Point.Empty, popup.Size));
                    popup.Save(Path.Combine("artifacts/image-editor", $"color-menu-{(palette == CapturePalette.Light ? "light" : "dark")}-{editor.DeviceDpi}.png"));
                }
                colors.HideDropDown();
                tools.Items["editorCrop"]!.PerformClick();
                tools.Items["editorConfirmCrop"]!.PerformClick();
                Check(!editor.IsEditorCropPending && editor.Canvas.Size == image.Size, "confirm full-frame crop preserves all pixels");
                tools.OverflowButton.ShowDropDown();
                await Task.Delay(30);
                VerifyMenuSpacing(tools.OverflowButton.DropDown, tools);
                using (Bitmap popup = new(tools.OverflowButton.DropDown.Width, tools.OverflowButton.DropDown.Height))
                {
                    tools.OverflowButton.DropDown.DrawToBitmap(popup, new Rectangle(Point.Empty, popup.Size));
                    popup.Save(Path.Combine("artifacts/image-editor", $"overflow-short-{(palette == CapturePalette.Light ? "light" : "dark")}-{editor.DeviceDpi}.png"));
                }
                using (Bitmap live = new Screenshot { RemoveOutsideScreenArea = false }.CaptureRectangle(PhysicalClient(tools.OverflowButton.DropDown)))
                    live.Save(Path.Combine("artifacts/image-editor", $"menu-spacing-{(palette == CapturePalette.Light ? "light" : "dark")}-{editor.DeviceDpi}.png"));
                tools.OverflowButton.HideDropDown();
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

    private static void VerifyBorderlessFeedback(ToolStrip strip, ToolStripItem item)
    {
        object renderer = strip.Renderer;
        IDictionary states = (IDictionary)renderer.GetType().GetField("states", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        object state = states[item]!;
        UiTween hover = (UiTween)state.GetType().GetField("Hover", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        Message pointerMove = Message.Create(strip.Handle, 0x200, 0, 0);
        Application.FilterMessage(ref pointerMove);
        typeof(ToolStripItem).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(item, [EventArgs.Empty]);
        hover.SnapTo(1);
        item.Select();
        using Bitmap frame = new(item.Width, item.Height);
        using (Graphics g = Graphics.FromImage(frame))
        {
            g.Clear(strip.BackColor);
            strip.Renderer.DrawOverflowButtonBackground(new ToolStripItemRenderEventArgs(g, item));
        }
        CapturePalette palette = strip.BackColor.ToArgb() == CapturePalette.Light.Background.ToArgb() ? CapturePalette.Light : CapturePalette.Dark;
        int accentPixels = 0;
        for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
                if (frame.GetPixel(x, y).ToArgb() == palette.Accent.ToArgb()) accentPixels++;
        Check(accentPixels == 0, "hovering the selected more-tools button paints no decorative blue outline");
        hover.SnapTo(0);
    }

    private static void VerifyIconPadding(ToolStrip strip)
    {
        float dpi = strip.DeviceDpi / 96f;
        foreach (ToolStripItem item in strip.Items.Cast<ToolStripItem>().Where(item => item.Placement == ToolStripItemPlacement.Main &&
            item.DisplayStyle == ToolStripItemDisplayStyle.ImageAndText && item.Image != null))
        {
            Rectangle Ink(bool text)
            {
                using Bitmap mask = new(item.Width, item.Height);
                using (Graphics g = Graphics.FromImage(mask))
                {
                    g.Clear(Color.Magenta);
                    if (text) strip.Renderer.DrawItemText(new ToolStripItemTextRenderEventArgs(g, item, item.Text,
                        new Rectangle(Point.Empty, item.Size), Color.Black, item.Font, TextFormatFlags.Left));
                    else strip.Renderer.DrawItemImage(new ToolStripItemImageRenderEventArgs(g, item, item.Image,
                        new Rectangle(Point.Empty, strip.ImageScalingSize)));
                }
                int left = mask.Width, right = -1;
                for (int y = 0; y < mask.Height; y++)
                    for (int x = 0; x < mask.Width; x++)
                        if (mask.GetPixel(x, y).ToArgb() != Color.Magenta.ToArgb()) { left = Math.Min(left, x); right = Math.Max(right, x); }
                return right >= left ? Rectangle.FromLTRB(left, 0, right + 1, mask.Height) : Rectangle.Empty;
            }
            Rectangle icon = Ink(false), label = Ink(true);
            Check(!icon.IsEmpty && !label.IsEmpty && icon.Left >= 16 * dpi - 1 &&
                label.Left - icon.Right >= 8 * dpi - 1 && item.Width - 4 * dpi - label.Right >= 12 * dpi - 1,
                item.Name + ": rendered icon has leading space, an 8px label gap and trailing text space");
        }
    }

    private static void VerifyTextBalance(ToolStrip strip)
    {
        foreach (ToolStripItem item in strip.Items.Cast<ToolStripItem>().Where(item => item.Placement == ToolStripItemPlacement.Main &&
            item.DisplayStyle == ToolStripItemDisplayStyle.Text))
        {
            using Bitmap mask = new(item.Width, item.Height);
            using (Graphics g = Graphics.FromImage(mask))
            {
                g.Clear(Color.Magenta);
                strip.Renderer.DrawItemText(new ToolStripItemTextRenderEventArgs(g, item, item.Text,
                    new Rectangle(Point.Empty, item.Size), Color.Black, item.Font, TextFormatFlags.Left));
            }
            int left = mask.Width, right = -1;
            for (int y = 0; y < mask.Height; y++)
                for (int x = 0; x < mask.Width; x++)
                    if (mask.GetPixel(x, y).ToArgb() != Color.Magenta.ToArgb()) { left = Math.Min(left, x); right = Math.Max(right, x); }
            int imbalance = Math.Abs(left - (item.Width - right - 1));
            Check(right >= left && imbalance <= 2, item.Name + ": visible text has equal left/right space (difference=" + imbalance + "px)");
        }
    }

    private static void VerifyMenuSpacing(ToolStrip menu, ToolStrip? overflowOwner = null)
    {
        float dpi = menu.DeviceDpi / 96f;
        int required = (int)Math.Round(128 * dpi);
        // Native overflow keeps its displayed items owned by the toolbar, rather than in menu.Items.
        ToolStripItem[] displayed = menu.Items.Cast<ToolStripItem>().Concat(
            (overflowOwner?.Items.Cast<ToolStripItem>() ?? [])
                .Where(item => item.GetCurrentParent() == menu)).Distinct().ToArray();
        foreach (ToolStripControlHost host in displayed.OfType<ToolStripControlHost>().Where(item => item.Available))
            required = Math.Max(required, host.Width + host.Margin.Horizontal);
        foreach (ToolStripItem item in displayed.Where(item => item.Available && item is not ToolStripSeparator && item is not ToolStripControlHost))
        {
            Rectangle Ink(string? text)
            {
                using Bitmap mask = new(item.Width, item.Height);
                using (Graphics g = Graphics.FromImage(mask))
                {
                    g.Clear(Color.Magenta);
                    if (text != null) menu.Renderer.DrawItemText(new ToolStripItemTextRenderEventArgs(g, item, text,
                        new Rectangle(Point.Empty, item.Size), Color.Black, item.Font, TextFormatFlags.Left));
                    else menu.Renderer.DrawItemImage(new ToolStripItemImageRenderEventArgs(g, item, item.Image,
                        new Rectangle(Point.Empty, menu.ImageScalingSize)));
                }
                int left = mask.Width, right = -1;
                for (int y = 0; y < mask.Height; y++)
                    for (int x = 0; x < mask.Width; x++)
                        if (mask.GetPixel(x, y).ToArgb() != Color.Magenta.ToArgb()) { left = Math.Min(left, x); right = Math.Max(right, x); }
                return right >= left ? Rectangle.FromLTRB(left, 0, right + 1, mask.Height) : Rectangle.Empty;
            }
            Rectangle label = Ink(item.Text ?? string.Empty);
            int accessory = item is ToolStripDropDownItem { HasDropDownItems: true } || item is ToolStripMenuItem { Tag: Color } ? (int)Math.Round(20 * dpi) : 0;
            string shortcut = item is ToolStripMenuItem command ? command.ShortcutKeyDisplayString ?? string.Empty : string.Empty;
            int shortcutWidth = shortcut.Length == 0 ? 0 : TextRenderer.MeasureText(shortcut, item.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int textWidth = TextRenderer.MeasureText(item.Text, item.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            required = Math.Max(required, textWidth + (int)Math.Round(54 * dpi) + accessory + (shortcutWidth > 0 ? shortcutWidth + (int)Math.Round(24 * dpi) : 0));
            Check(!label.IsEmpty && item.Width - label.Right >= 12 * dpi - 1, "popup label keeps trailing space: " + item.Text);
            if (item.Image != null)
            {
                Rectangle icon = Ink(null);
                Check(!icon.IsEmpty && icon.Left >= 13 * dpi - 1 && label.Left - icon.Right >= 8 * dpi - 1,
                    "popup icon has leading space and an 8px label gap: " + item.Text);
            }
            if (shortcut.Length > 0)
            {
                Rectangle keys = Ink(shortcut);
                Check(!keys.IsEmpty && keys.Left - label.Right >= 24 * dpi - 1 && item.Width - keys.Right >= 12 * dpi - 1,
                    "popup shortcut stays separated and padded: " + item.Text);
            }
        }
        Console.WriteLine($"MENU WIDTH: actual={menu.Width}, content={required}, padding={menu.Padding.Horizontal}, DPI={menu.DeviceDpi}");
        Check(menu.Width <= required + menu.Padding.Horizontal + 4 * dpi,
            "popup width fits its current content instead of retaining a large fixed minimum");
    }

    private static async Task VerifyFeedbackAsync(ToolStrip strip)
    {
        ToolStripItem item = strip.Items["editorCopy"]!;
        object renderer = strip.Renderer;
        IDictionary states = (IDictionary)renderer.GetType().GetField("states", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        object state = states[item]!;
        UiTween hover = (UiTween)state.GetType().GetField("Hover", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        Rectangle bounds = item.Bounds;
        Font font = item.Font;
        hover.SnapTo(0);
        hover.SpringTo(1, ButtonElevation.ResponseMilliseconds);
        if (UiMotion.Enabled)
        {
            await Task.Delay(45);
            float value = hover.Value, velocity = hover.Velocity;
            hover.SpringTo(0, ButtonElevation.ResponseMilliseconds);
            Console.WriteLine($"REVERSAL: before={value:F4}/{velocity:F4}, after={hover.Value:F4}/{hover.Velocity:F4}, visible={strip.Visible}, handle={strip.IsHandleCreated}");
            Check(value > 0 && value < 1 && Math.Abs(value - hover.Value) < .001f && Math.Abs(velocity - hover.Velocity) < .001f,
                "hover reversal preserves its current presentation and velocity");
        }
        int layouts = 0;
        LayoutEventHandler count = (_, _) => layouts++;
        strip.Layout += count;
        List<double> samples = new();
        try
        {
            for (int frame = 0; frame < 32; frame++)
            {
                Stopwatch timer = Stopwatch.StartNew();
                hover.SnapTo(frame / 31f);
                strip.Update(); // Includes the live material alpha pass when DWM is enabled.
                samples.Add(timer.Elapsed.TotalMilliseconds);
            }
        }
        finally { strip.Layout -= count; hover.SnapTo(0); strip.Update(); }
        double average = samples.Average(), p95 = samples.Order().ElementAt((int)(samples.Count * .95));
        Console.WriteLine($"PERF: toolbar feedback at {strip.DeviceDpi} DPI: mean={average:F3}ms, p95={p95:F3}ms, layout passes={layouts}");
        Check(layouts == 0 && item.Bounds == bounds && ReferenceEquals(item.Font, font),
            "feedback preserves toolbar layout, label size and font weight");
        Check(average < 16.7, "measured toolbar repaint stays within the 60Hz frame budget on this desktop");
    }

    private static async Task CheckOverflowAsync(RegionCaptureForm editor, ToolStrip tools, CapturePalette palette)
    {
        float dpi = editor.DeviceDpi / 96f;
        var original = tools.Items.Cast<ToolStripItem>().ToDictionary(item => item, item => (item.Size, item.DisplayStyle, item.Text));
        ToolStripOverflowButton more = tools.OverflowButton;
        Console.WriteLine($"overflow button: size={more.Size}; preferred={more.GetPreferredSize(Size.Empty)}; padding={more.Padding}; label={more.AccessibleName}");
        Check(more.Width >= 32 * dpi && more.Height >= 32 * dpi && more.AccessibleName == ShareX.Roll.Localization.UiText.L("EditorMoreTools"),
            "more tools has a comfortable labeled target instead of a narrow overflow arrow");
        Check(Math.Abs(more.Width - 48 * dpi) <= 2 * dpi && more.Height <= tools.Height,
            "native overflow retains the same width as the other icon buttons");
        more.ShowDropDown();
        await Task.Delay(100);
        ToolStripDropDown menu = more.DropDown;
        ToolStripItem[] items = tools.Items.Cast<ToolStripItem>().Where(item => item.Available && item.GetCurrentParent() == menu).ToArray();
        Console.WriteLine($"overflow opened: visible={menu.Visible}; bounds={menu.Bounds}; items={items.Length}");
        foreach (ToolStripItem item in items) Console.WriteLine($"overflow item: {item.Text}; {item.Bounds}; {item.DisplayStyle}");
        Check(menu.Visible && items.Length > 0 && items.All(item => item.DisplayStyle == ToolStripItemDisplayStyle.ImageAndText && !string.IsNullOrWhiteSpace(item.Text)),
            "overflow presents every command with an icon and a readable label");
        VerifyMenuSpacing(menu, tools);
        Check(items.All(item => item.Height <= 40 * dpi && item.Height >= 32 * dpi),
            "overflow uses compact readable rows instead of oversized button spacing");
        Check(items.Select(item => item.Bounds.Left).Distinct().Count() == 1 &&
            items.Zip(items.Skip(1), (a, b) => a.Bounds.Bottom <= b.Bounds.Top).All(value => value),
            "overflow tools form a single aligned column without a wrapping icon grid");
        Check(Screen.FromControl(editor).WorkingArea.Contains(menu.Bounds), "overflow menu stays inside the available screen");
        ToolStripItem? next = menu.GetNextItem(items[0], ArrowDirection.Down);
        Check(next == items.ElementAtOrDefault(1), "native downward keyboard navigation follows the visible menu order");
        using (Bitmap popup = new(menu.Width, menu.Height))
        {
            menu.DrawToBitmap(popup, new Rectangle(Point.Empty, menu.Size));
            Check(popup.GetPixel((int)(3 * dpi), menu.Height / 2).ToArgb() == palette.Background.ToArgb(),
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
        Check(ReferenceEquals(editMenu.DropDown.Renderer, menu.Renderer) && editMenu.DropDown.Font.Equals(menu.Font) && HasRoundedSurface(editMenu.DropDown),
            "edit submenu shares the overflow's renderer, typography and rounded surface");
        ToolStripItem[] editCommands = editMenu.DropDownItems.Cast<ToolStripItem>().Where(item => item is not ToolStripSeparator).ToArray();
        VerifyMenuSpacing(editMenu.DropDown);
        Check(editCommands.All(item => EditorIcons.HasIcon(item) && item.Height <= 40 * dpi),
            "all edit commands use the same monochrome icon family and compact rows");
        using (Bitmap popup = new(editMenu.DropDown.Width, editMenu.DropDown.Height))
        {
            editMenu.DropDown.DrawToBitmap(popup, new Rectangle(Point.Empty, popup.Size));
            Check(popup.GetPixel((int)(3 * dpi), popup.Height / 2).ToArgb() == palette.Background.ToArgb(),
                "nested menu background follows both editor appearances");
            string theme = palette == CapturePalette.Light ? "light" : "dark";
            popup.Save(Path.Combine("artifacts/image-editor", $"edit-submenu-{theme}-{editor.DeviceDpi}.png"));
        }
        // DrawToBitmap ignores the native popup Region and outside shadow. Capture the composed desktop too.
        using (Bitmap live = new Screenshot { RemoveOutsideScreenArea = false }.CaptureRectangle(
            Rectangle.Union(PhysicalClient(editor), Rectangle.Union(PhysicalClient(menu), PhysicalClient(editMenu.DropDown)))))
            live.Save(Path.Combine("artifacts/image-editor", $"editor-live-{(palette == CapturePalette.Light ? "light" : "dark")}-{editor.DeviceDpi}.png"));
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
        ToolStripDropDownItem styleMenu = (ToolStripDropDownItem)tools.Items["editorStyle"]!;
        more.ShowDropDown();
        styleMenu.ShowDropDown();
        await Task.Delay(50);
        Check(styleMenu.DropDown.Visible, "style popup remains accessible after the menu spacing change");
        VerifyMenuSpacing(styleMenu.DropDown);
        Check(styleMenu.DropDown.Items.OfType<ToolStripControlHost>().Where(item => item.Available)
            .All(item => styleMenu.DropDown.ClientRectangle.Contains(item.Bounds)), "style fields stay entirely inside the popup");
        ShareX.HelpersLib.ToolStripLabeledNumericUpDown pixelSize = styleMenu.DropDown.Items
            .OfType<ShareX.HelpersLib.ToolStripLabeledNumericUpDown>().Single(item => item.Available);
        decimal originalValue = pixelSize.Content.Value;
        pixelSize.Content.Value = originalValue + 1;
        Check(editor.Options.AnnotationOptions.PixelateSize == (int)pixelSize.Content.Value,
            "style field still updates the original pixelate option after popup sizing changes");
        pixelSize.Content.Value = originalValue;
        using (Bitmap popup = new(styleMenu.DropDown.Width, styleMenu.DropDown.Height))
        {
            styleMenu.DropDown.DrawToBitmap(popup, new Rectangle(Point.Empty, popup.Size));
            popup.Save(Path.Combine("artifacts/image-editor", $"style-menu-{(palette == CapturePalette.Light ? "light" : "dark")}-{editor.DeviceDpi}.png"));
        }
        styleMenu.HideDropDown();
        more.HideDropDown();
        more.ShowDropDown();
        await Task.Delay(30);
        editor.Width += (int)(20 * dpi);
        await Task.Delay(30);
        Check(!menu.Visible, "resizing dismisses overflow and restores normal toolbar layout");
    }
}
