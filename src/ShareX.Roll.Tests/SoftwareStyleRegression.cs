using ShareX.Roll;
using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class SoftwareStyleRegression
{
    private const BindingFlags Members = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint handle, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint handle, ref Point point);
    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
    [DllImport("gdi32.dll")]
    private static extern bool PtInRegion(nint region, int x, int y);

    private static void CheckButtonFace(Control control, bool claude)
    {
        Point middle = new(control.Width / 2, control.Height / 2);
        Point gutter = new(0, middle.Y);
        Check(claude ? control.Region is { } region && region.IsVisible(middle) && !region.IsVisible(gutter) : control.Region == null,
            $"{control.Name} uses the expected pointer bounds at {control.DeviceDpi} DPI");
    }

    private static async Task VerifyMouseRoutingAsync(MainForm main)
    {
        using Form probe = new() { ClientSize = new Size(350, 200), StartPosition = FormStartPosition.CenterParent, TopMost = true, ShowInTaskbar = false };
        AboutDocumentButton button = new() { Text = "Button", Bounds = new Rectangle(45, 35, 250, 64) };
        Button primary = new() { Text = "Primary", Bounds = new Rectangle(45, 110, 250, 64) };
        probe.Controls.Add(button);
        probe.Controls.Add(primary);
        SettingsColors.Apply(probe, CapturePalette.ClaudeLight, primary);
        int entered = 0, clicked = 0;
        button.MouseEnter += (_, _) => entered++;
        button.Click += (_, _) => clicked++;
        Point original = Cursor.Position;
        try
        {
            probe.Show(main);
            probe.ForceActivate();
            Cursor.Position = probe.PointToScreen(new Point(5, 5));
            await Task.Delay(80);
            entered = 0;
            Point gutter = button.PointToScreen(new Point(2, button.Height / 2));
            Point middle = button.PointToScreen(new Point(button.Width / 2, button.Height / 2));
            Cursor.Position = gutter;
            await Task.Delay(90);
            Check(WindowFromPoint(gutter) == probe.Handle, "Windows hits the parent in the button gutter");
            ClickPointerTarget(probe, gutter);
            await Task.Delay(120);
            Check(entered == 0 && clicked == 0, "moving and clicking in the gutter neither hovers nor activates the button");
            using Bitmap idle = new(button.Width, button.Height);
            button.DrawToBitmap(idle, button.ClientRectangle);
            Cursor.Position = middle;
            await Task.Delay(90);
            Check(WindowFromPoint(middle) == button.Handle, "Windows hits the button at the visible center");
            ClickPointerTarget(button, middle);
            await Task.Delay(120);
            Check(entered == 1 && clicked == 1, "moving and clicking on the visible face hovers and activates once");
            // Keep pixel verification independent of the desktop cursor and tween timing.
            ((UiTween)typeof(AboutDocumentButton).GetField("hover", Members)!.GetValue(button)!).SnapTo(1);
            using Bitmap hovered = new(button.Width, button.Height);
            button.DrawToBitmap(hovered, button.ClientRectangle);
            VerifyTextAction(button, idle, hovered);

            using Bitmap primaryIdle = new(primary.Width, primary.Height);
            primary.DrawToBitmap(primaryIdle, primary.ClientRectangle);
            bool motion = UiMotion.UserEnabled;
            try
            {
                UiMotion.UserEnabled = false;
                typeof(Control).GetMethod("OnMouseEnter", Members)!.Invoke(primary, [EventArgs.Empty]);
            }
            finally { UiMotion.UserEnabled = motion; }
            using Bitmap primaryHovered = new(primary.Width, primary.Height);
            primary.DrawToBitmap(primaryHovered, primary.ClientRectangle);
            VerifyTextAction(primary, primaryIdle, primaryHovered, highlighted: true);
        }
        finally
        {
            probe.Close();
            Cursor.Position = original;
        }
    }

    private static void VerifyLinkArrow()
    {
        using Panel parent = new() { BackColor = CapturePalette.ClaudeLight.Surface };
        AboutLinkButton button = new("Account", "@sample", profile: true) { Size = new Size(250, 84) };
        parent.Controls.Add(button);
        button.ApplyPalette(CapturePalette.ClaudeLight);
        button.CreateControl();
        UiTween hover = (UiTween)typeof(AboutLinkButton).GetField("hover", Members)!.GetValue(button)!;
        using Bitmap idle = new(button.Width, button.Height);
        using Bitmap active = new(button.Width, button.Height);
        hover.SnapTo(0);
        button.DrawToBitmap(idle, button.ClientRectangle);
        hover.SnapTo(1);
        button.DrawToBitmap(active, button.ClientRectangle);

        float scale = button.DeviceDpi / 96f;
        RectangleF face = ButtonElevation.CompactFaceBounds(button.ClientSize, scale);
        float x = face.Right - ButtonSpacing.Horizontal * scale;
        float y = button.Height / 2f;
        Rectangle arrow = Rectangle.Ceiling(new RectangleF(x - 7 * scale, y - 7 * scale, 14 * scale, 14 * scale));
        Color background = CapturePalette.ClaudeLight.Surface;
        Color secondary = CapturePalette.ClaudeLight.Secondary, orange = CapturePalette.ClaudeLight.Accent;
        double maxCoverage = 0;
        bool matchingStroke = true;
        for (int row = arrow.Top; row < arrow.Bottom; row++)
        for (int column = arrow.Left; column < arrow.Right; column++)
        {
            Color before = idle.GetPixel(column, row), after = active.GetPixel(column, row);
            // Antialiasing can round two colors differently at an edge, even for the same stroke.
            double beforeCoverage = (double)(background.G - before.G) / (background.G - secondary.G);
            double afterCoverage = (double)(background.G - after.G) / (background.G - orange.G);
            maxCoverage = Math.Max(maxCoverage, beforeCoverage);
            matchingStroke &= Math.Abs(beforeCoverage - afterCoverage) < .03 &&
                Math.Abs(after.R - (background.R + (orange.R - background.R) * afterCoverage)) < 3 &&
                Math.Abs(after.B - (background.B + (orange.B - background.B) * afterCoverage)) < 3;
        }
        Check(maxCoverage > .5 && matchingStroke,
            "the about link arrow changes to the orange accent without changing its stroke");
    }

    private static void VerifyTextAction(Button button, Bitmap idle, Bitmap hovered, bool highlighted = false)
    {
        float scale = button.DeviceDpi / 96f;
        int x = button.Width / 2;
        int accent = CapturePalette.ClaudeLight.Accent.ToArgb();
        int ink = highlighted ? accent : CapturePalette.ClaudeLight.Secondary.ToArgb();
        int thin = (int)MathF.Round(scale), thick = (int)MathF.Round(2 * scale);
        int[] Rows(Bitmap image, int color) => Enumerable.Range(button.Height / 2, button.Height / 2)
            .Where(y => Enumerable.Range(x - 10, 21).All(column => image.GetPixel(column, y).ToArgb() == color)).ToArray();
        int[] idleRows = Rows(idle, ink), hoverRows = Rows(hovered, accent);
        Check(idleRows.Length == thin && hoverRows.Length == thick && idleRows[0] == hoverRows[0],
            $"Claude text action thickens its underline from {idleRows.Length} to {hoverRows.Length} pixels and applies the accent");
        int blankX = x + (int)MathF.Round(35 * scale);
        Color blank = idle.GetPixel(blankX, button.Height / 2);
        Check(blank == hovered.GetPixel(blankX, button.Height / 2) && blank.ToArgb() == button.Parent!.BackColor.ToArgb(),
            "Claude text action uses its parent's background in both states");
        if (highlighted)
        {
            Check(Enumerable.Range(button.Height / 4, idleRows[0] - button.Height / 4)
                .All(y => Enumerable.Range(x - 35, 71).All(column => idle.GetPixel(column, y) == hovered.GetPixel(column, y))),
                "highlighted action keeps the same orange text and glyph pixels on hover");
        }
    }

    private static void ClickPointerTarget(Control target, Point screen)
    {
        if (Cursor.Position == screen && InputHelpers.SendMouseClick()) return;
        // Restricted test desktops can block SendInput. Exercise the same mouse
        // handlers on the native window that Windows found at the real pointer.
        Point point = target.PointToClient(screen);
        nint packed = unchecked((ushort)point.X | ((ushort)point.Y << 16));
        SendMessage(target.Handle, 0x200, 0, packed); // WM_MOUSEMOVE
        SendMessage(target.Handle, 0x201, 1, packed); // WM_LBUTTONDOWN
        SendMessage(target.Handle, 0x202, 0, packed); // WM_LBUTTONUP
    }

    private static Bitmap CaptureLiveControl(Control control, Rectangle? bounds = null)
    {
        Rectangle area = bounds ?? control.ClientRectangle;
        Point start = control.PointToScreen(area.Location);
        Point end = control.PointToScreen(new Point(area.Right, area.Bottom));
        Check(LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref start) &&
            LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref end), "mapped " + control.Name + " to screen pixels");
        DwmFlush();
        nint previous = SetThreadDpiAwarenessContext((nint)(-4));
        try { return new Screenshot { RemoveOutsideScreenArea = false }.CaptureRectangle(Rectangle.FromLTRB(start.X, start.Y, end.X, end.Y)); }
        finally { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
    }

    private static async Task VerifyGroupDividerAsync(ScrollableControl viewport, SettingsGroup group,
        CapturePalette palette, string state, string? screenshot = null)
    {
        float scale = group.DeviceDpi / 96f;
        int inset = (int)MathF.Round(16 * scale);
        int bottom = viewport.PointToClient(group.PointToScreen(new Point(0, group.Height))).Y;
        viewport.AutoScrollPosition = new Point(0, Math.Max(0, -viewport.AutoScrollPosition.Y +
            bottom - viewport.ClientSize.Height + inset));
        await Task.Delay(70);

        int height = Math.Min(group.Height, (int)MathF.Ceiling((screenshot == null ? 36 : 92) * scale));
        Rectangle strip = new(0, group.Height - height, group.Width, height);
        using Bitmap frame = CaptureLiveControl(group, strip);
        int stroke = CaptureDrawing.BorderPixelWidth(scale);
        float desktopScale = frame.Width / (float)group.Width;
        bool resampled = desktopScale > 1.01f;
        int margin = (int)MathF.Ceiling(inset * desktopScale) + (resampled ? 1 : 0);
        int edge = (int)MathF.Ceiling(stroke * desktopScale);
        int tolerance = resampled ? ColorDistance(palette.Border, palette.Background) / 3 : 0;
        bool dividerMatches = true;
        // Allow blending near the ends of lines on DPI-unaware windows, and verify every interior column.
        for (int x = margin; x < frame.Width - margin; x++)
        {
            if (resampled)
                dividerMatches &= Enumerable.Range(frame.Height - edge - 1, edge + 1)
                    .Any(y => ColorDistance(frame.GetPixel(x, y), palette.Border) <= tolerance);
            else
                for (int y = frame.Height - stroke; y < frame.Height; y++)
                    dividerMatches &= frame.GetPixel(x, y).ToArgb() == palette.Border.ToArgb();
        }
        int outsideDivider = margin - (resampled ? 3 : 1);
        bool flatEdges = Enumerable.Range(0, edge).All(x =>
            ColorDistance(frame.GetPixel(x, frame.Height / 2), palette.Background) <= tolerance &&
            ColorDistance(frame.GetPixel(frame.Width - 1 - x, frame.Height / 2), palette.Background) <= tolerance);
        int ruleY = frame.Height - Math.Max(1, (edge + 1) / 2);
        bool insetMatches = ColorDistance(frame.GetPixel(outsideDivider, ruleY), palette.Background) <= tolerance &&
            ColorDistance(frame.GetPixel(frame.Width - 1 - outsideDivider, ruleY), palette.Background) <= tolerance;
        if (screenshot != null) frame.Save(screenshot);
        Check(dividerMatches && insetMatches && flatEdges,
            $"{group.Name} {state} has one continuous inset divider and flat side edges at {group.DeviceDpi} DPI");
    }

    private static async Task VerifySettingsDividersAsync(SettingsPage page, CapturePalette palette, string output)
    {
        ScrollableControl viewport = (ScrollableControl)typeof(SettingsPage).GetField("scroller", Members)!.GetValue(page)!;
        foreach (string name in new[] { "softwareStyleRow", "transparencyRow", "motionRow", "languageRow", "aboutSoftware" }
            .Concat(ApplicationPreferences.Actions.Select(action => action.Kind + "ShortcutRow")))
            await VerifyGroupDividerAsync(viewport, Find<SettingsGroup>(page, name), palette, "at rest");

        SettingsGroup saveLocation = Find<SettingsGroup>(page, "saveLocationGroup");
        MotionToggle folderToggle = Find<MotionToggle>(page, "defaultSaveFolderToggle");
        bool folderWasEnabled = folderToggle.Checked;
        try
        {
            folderToggle.Checked = false;
            await VerifyGroupDividerAsync(viewport, saveLocation, palette, "without path");
            folderToggle.Checked = true;
            Check(Find<Control>(page, "saveFolderPathRow").Visible, "the save-folder controls show when enabled");
            await VerifyGroupDividerAsync(viewport, saveLocation, palette, "with path");
        }
        finally { folderToggle.Checked = folderWasEnabled; }

        SettingsGroup group = Find<SettingsGroup>(page, "scrollingOptionsRow");
        SettingsDisclosure disclosure = Find<SettingsDisclosure>(page, "scrollingOptionsButton");
        SettingsReveal reveal = Find<SettingsReveal>(page, "scrollingReveal");
        SettingsChoice method = Find<SettingsChoice>(page, "cbScrollMethod");
        string shade = palette == CapturePalette.ClaudeDark ? "dark" : "light";

        await VerifyGroupDividerAsync(viewport, group, palette, "collapsed");
        disclosure.PerformClick();
        reveal.Finish();
        MotionToggle autoSave = Find<MotionToggle>(page, "cbAutoUpload");
        Check(disclosure.Expanded && autoSave.Visible && autoSave.DeviceDpi == group.DeviceDpi,
            "the last scrolling option is visible after expansion");
        await VerifyGroupDividerAsync(viewport, group, palette, "expanded",
            Path.Combine(output, $"scrolling-expanded-{shade}-{group.DeviceDpi}.png"));
        Label caption = autoSave.Parent!.Controls.OfType<Label>().Single();
        Check(caption.Width >= TextRenderer.MeasureText(caption.Text, caption.Font).Width,
            $"the last option's full label fits at {group.DeviceDpi} DPI");
        ((UiTween)typeof(MotionToggle).GetField("hover", Members)!.GetValue(autoSave)!).SnapTo(0);
        VerifyToggleTrack(autoSave, autoSave.Checked ? palette.Accent : palette.Surface,
            autoSave.Checked ? palette.Accent : palette.FieldBorder, live: Application.HighDpiMode == HighDpiMode.PerMonitorV2);
        int previousMethod = method.SelectedIndex;
        method.SelectedIndex = (int)ScrollMethod.PageDown;
        Check(!Find<NumericUpDown>(page, "nudScrollAmount").Visible, "Page Down hides the unused scroll amount");
        await VerifyGroupDividerAsync(viewport, group, palette, "expanded with Page Down");
        method.SelectedIndex = previousMethod;
        disclosure.PerformClick();
        reveal.Finish();
        await VerifyGroupDividerAsync(viewport, group, palette, "re-collapsed");
    }

    private static void VerifyFieldSurface(SettingsChoice style)
    {
        // The forced 96-DPI fixture virtualizes desktop coordinates.
        if (Application.HighDpiMode == HighDpiMode.DpiUnaware) return;
        using Bitmap frame = CaptureLiveControl(style);
        int y = frame.Height / 5;
        Color textSide = frame.GetPixel(frame.Width / 5, y);
        Color arrowSide = frame.GetPixel(frame.Width * 4 / 5, y);
        Check(textSide == arrowSide && textSide.ToArgb() == CapturePalette.ClaudeLight.Surface.ToArgb(),
            $"the live selection field uses one surface across its text and arrow sides ({textSide}, {arrowSide})");
    }

    private static void VerifyBorderPixels(Control control, Color color, float width)
    {
        if (Application.HighDpiMode == HighDpiMode.DpiUnaware) return;
        using Bitmap frame = CaptureLiveControl(control);
        float scale = control.DeviceDpi / 96f;
        int expected = (int)MathF.Round(width * scale, MidpointRounding.AwayFromZero);
        int depth = (int)MathF.Ceiling(12 * scale);
        int x = frame.Width / 2, y = frame.Height / 2, ink = color.ToArgb();
        int[] counts = [
            Enumerable.Range(0, depth).Count(i => frame.GetPixel(x, i).ToArgb() == ink),
            Enumerable.Range(0, depth).Count(i => frame.GetPixel(x, frame.Height - 1 - i).ToArgb() == ink),
            Enumerable.Range(0, depth).Count(i => frame.GetPixel(i, y).ToArgb() == ink),
            Enumerable.Range(0, depth).Count(i => frame.GetPixel(frame.Width - 1 - i, y).ToArgb() == ink)
        ];
        Check(counts.All(count => count == expected),
            $"{control.Name} border pixels on top/bottom/left/right at {control.DeviceDpi} DPI: {string.Join("/", counts)} (expected {expected})");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private static int ColorDistance(Color a, Color b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    private static void VerifyRoundedCorner(Control control, Bitmap bitmap, RectangleF hitFace, float radius,
        Color background, Color fill, Color ink)
    {
        nint region = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        try
        {
            Check(NativeMethods.GetWindowRgn(control.Handle, region) > 0, $"{control.Name} has a native hit region");
            int right = (int)MathF.Ceiling(hitFace.Left + radius), bottom = (int)MathF.Ceiling(hitFace.Top + radius);
            int cut = 0, softened = 0;
            for (int corner = 0; corner < 4; corner++)
            for (int y = 0; y < bottom; y++)
            for (int x = 0; x < right; x++)
            {
                int px = (corner & 1) == 0 ? x : bitmap.Width - 1 - x;
                int py = (corner & 2) == 0 ? y : bitmap.Height - 1 - y;
                Color pixel = bitmap.GetPixel(px, py);
                if (ColorDistance(pixel, background) <= 2) continue;
                if (!PtInRegion(region, px, py)) cut++;
                if (pixel.ToArgb() != ink.ToArgb() && pixel.ToArgb() != fill.ToArgb()) softened++;
            }
            Check(cut == 0 && softened > 4,
                $"{control.Name} keeps its antialiased corner inside the hit region at {control.DeviceDpi} DPI ({cut} clipped, {softened} blended)");
        }
        finally { NativeMethods.DeleteObject(region); }
    }

    private static void VerifyAppearanceStates(SettingsPage page, CapturePalette palette)
    {
        int dpi = page.DeviceDpi;
        float scale = dpi / 96f;
        ScrollableControl viewport = (ScrollableControl)typeof(SettingsPage).GetField("scroller", Members)!.GetValue(page)!;
        SettingsChoice style = Find<SettingsChoice>(page, "softwareStyleSelector");
        SettingsChoice language = Find<SettingsChoice>(page, "languageSelector");
        const int side = 5;
        Color ChoiceFill(SettingsChoice field)
        {
            viewport.ScrollControlIntoView(field);
            using Bitmap bitmap = RenderControl(field);
            int y = (int)MathF.Ceiling(5 * scale);
            Color fill = bitmap.GetPixel(bitmap.Width / side, y);
            Check(fill == bitmap.GetPixel(bitmap.Width * (side - 1) / side, y), $"{field.Name} paints one surface at {dpi} DPI");
            return fill;
        }
        void Hover(Control control, bool hovered) =>
            typeof(Control).GetMethod(hovered ? "OnMouseEnter" : "OnMouseLeave", Members)!.Invoke(control, [EventArgs.Empty]);

        Check(style.DeviceDpi == dpi && language.DeviceDpi == dpi &&
            style.Width == language.Width && style.Height == language.Height && style.Right == language.Right,
            $"appearance selectors share dimensions and right alignment at {dpi} DPI");
        Hover(style, false);
        Hover(language, false);
        Check(ChoiceFill(style).ToArgb() == palette.Surface.ToArgb() && ChoiceFill(language).ToArgb() == palette.Surface.ToArgb(),
            $"both idle appearance selectors use the same paper at {dpi} DPI");
        style.Focus();
        Hover(language, true);
        Check(ChoiceFill(language).ToArgb() == CaptureDrawing.Blend(palette.Surface, palette.Hover, .4f).ToArgb(),
            $"pointer hover lightly shades the language field at {dpi} DPI");
        Hover(language, false);
        using (Bitmap field = RenderControl(language))
            VerifyRoundedCorner(language, field, new RectangleF(scale, scale, language.Width - 2 * scale, language.Height - 2 * scale),
                10 * scale, palette.Background, palette.Surface, palette.FieldBorder);

        TableLayoutPanel options = Find<TableLayoutPanel>(page, "appearanceOptions");
        ThemeOption[] cards = options.Controls.OfType<ThemeOption>().OrderBy(option => option.Left).ToArray();
        Check(cards.Length == 3 && cards.Count(option => option.Checked) == 1,
            $"the three color mode cards have one selection at {dpi} DPI");
        foreach (ThemeOption option in cards)
        {
            option.SettleSelection();
            ((UiTween)typeof(ThemeOption).GetField("hover", Members)!.GetValue(option)!).SnapTo(0);
            viewport.ScrollControlIntoView(option);
            using Bitmap bitmap = RenderControl(option);
            RectangleF face = ButtonElevation.CompactFaceBounds(option.ClientSize, scale);
            int centerX = bitmap.Width / 2;
            int centerY = bitmap.Height / 2;
            Color fill = option.Checked ? palette.Tint : palette.Surface;
            int cardColor = fill.ToArgb();
            int sampleOffset = (int)MathF.Round(25 * scale);
            Check(bitmap.GetPixel(centerX, centerY - sampleOffset).ToArgb() == cardColor &&
                bitmap.GetPixel(centerX, centerY + sampleOffset).ToArgb() == cardColor,
                $"{option.Name} uses the expected selected or resting fill at {dpi} DPI");

            bool focused = option.Focused && (bool)typeof(Control).GetProperty("ShowFocusCues", Members)!.GetValue(option)!;
            int borderWidth = CaptureDrawing.BorderPixelWidth((focused ? 1.75f : 1) * scale);
            Color edge = option.Checked || focused ? palette.Accent : palette.Border;
            VerifyRoundedCorner(option, bitmap, face, ThemeOption.ClaudeCornerRadius * scale,
                palette.Background, fill, edge);
            face.Inflate(-2, -2);
            int left = (int)MathF.Ceiling(face.Left), right = (int)MathF.Floor(face.Right) - 1;
            int top = (int)MathF.Ceiling(face.Top), bottom = (int)MathF.Floor(face.Bottom) - 1;
            Check(Enumerable.Range(0, borderWidth + 1).All(offset =>
            {
                int expected = offset < borderWidth ? edge.ToArgb() : cardColor;
                return bitmap.GetPixel(centerX, top + offset).ToArgb() == expected &&
                    bitmap.GetPixel(centerX, bottom - offset).ToArgb() == expected &&
                    bitmap.GetPixel(left + offset, centerY).ToArgb() == expected &&
                    bitmap.GetPixel(right - offset, centerY).ToArgb() == expected;
            }), $"{option.Name} has a {borderWidth}px outline with the expected color on all four sides at {dpi} DPI");

            CapturePalette illustration = option.Mode == AppearanceMode.Dark ? CapturePalette.ClaudeDark : CapturePalette.ClaudeLight;
            int iconLeft = (int)MathF.Round(face.Left + ButtonSpacing.Horizontal * scale);
            int iconTop = centerY - (int)MathF.Round(19 * scale);
            bool warmInk = Enumerable.Range(iconTop, (int)MathF.Round(38 * scale)).Any(y =>
                Enumerable.Range(iconLeft, (int)MathF.Round(50 * scale)).Any(x =>
                {
                    Color pixel = bitmap.GetPixel(x, y), accent = illustration.IllustrationAccent;
                    return Math.Abs(pixel.R - accent.R) <= 10 && Math.Abs(pixel.G - accent.G) <= 10 &&
                        Math.Abs(pixel.B - accent.B) <= 10;
                }));
            Check(warmInk, $"{option.Name} renders the terracotta paper illustration at {dpi} DPI");

            int cornerX = (int)MathF.Round(face.Right - 14 * scale);
            int cornerY = (int)MathF.Round(face.Top + 11 * scale);
            int diameter = (int)MathF.Round(8 * scale);
            Check(Enumerable.Range(cornerY - diameter / 2, diameter).All(y =>
                Enumerable.Range(cornerX - diameter / 2, diameter).All(x =>
                    bitmap.GetPixel(x, y).ToArgb() != palette.Accent.ToArgb())),
                $"{option.Name} leaves out the old checkmark at {dpi} DPI");
        }
        using (Bitmap row = RenderControl(options))
        {
            for (int index = 1; index < cards.Length; index++)
            {
                int gutter = (cards[index - 1].Right + cards[index].Left) / 2;
                Check(cards[index - 1].Right < cards[index].Left &&
                    row.GetPixel(gutter, row.Height / 2).ToArgb() == palette.Background.ToArgb(),
                    $"the gap before {cards[index].Name} shows the page background at {dpi} DPI");
            }
        }

        MotionToggle toggle = Find<MotionToggle>(page, "animationsToggle");
        MotionToggle disabled = Find<MotionToggle>(page, "transparencyToggle");
        viewport.ScrollControlIntoView(toggle);
        UiTween position = (UiTween)typeof(MotionToggle).GetField("position", Members)!.GetValue(toggle)!;
        float original = position.Value;
        try
        {
            Hover(toggle, false);
            position.SnapTo(0);
            VerifyToggleTrack(toggle, palette.Surface, palette.FieldBorder);
            position.SnapTo(1);
            VerifyToggleTrack(toggle, palette.Accent, palette.Accent);
        }
        finally { position.SnapTo(original); }
        Check(!disabled.Enabled, "transparency remains unavailable in the Claude style");
        VerifyToggleTrack(disabled, palette.Surface, palette.Border);
        viewport.AutoScrollPosition = Point.Empty;
    }

    private static void VerifyToggleTrack(MotionToggle control, Color interior, Color border, bool live = false)
    {
        float scale = control.DeviceDpi / 96f;
        RectangleF bounds = MotionToggle.TrackBounds(control.ClientSize, scale);
        int top = (int)MathF.Ceiling(bounds.Top);
        int bottom = (int)MathF.Floor(bounds.Bottom) - 1;
        int width = (int)MathF.Round(scale, MidpointRounding.AwayFromZero);
        control.Update();
        using Bitmap bitmap = live ? CaptureLiveControl(control) : RenderControl(control);
        int x = bitmap.Width / 2;
        Check(bitmap.GetPixel(x, top).ToArgb() == border.ToArgb() &&
            bitmap.GetPixel(x, bottom).ToArgb() == border.ToArgb() &&
            bitmap.GetPixel(x, top + width).ToArgb() == interior.ToArgb(),
            $"{control.Name} track and outline match its enabled and checked state at {control.DeviceDpi} DPI");
    }

    internal static async Task RunIntermediateDpiAsync(MainForm main, SoftwareStyle theme)
    {
        string output = Path.GetFullPath(theme switch
        {
            SoftwareStyle.MetroClassic => "artifacts/metro-style/simulated-dpi",
            SoftwareStyle.WinUI => "artifacts/winui-style/simulated-dpi",
            SoftwareStyle.MaterialYou => "artifacts/material-style/simulated-dpi",
            _ => "artifacts/software-style/simulated-dpi"
        });
        Directory.CreateDirectory(output);
        FieldInfo deviceDpi = typeof(Control).GetField("_deviceDpi", Members)
            ?? throw new InvalidOperationException("The WinForms DPI backing field has changed.");
        bool originalMotion = UiMotion.UserEnabled;
        try
        {
            foreach (int dpi in new[] { 120, 144, 168 })
            foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                float scale = dpi / 96f;
                using Form host = new()
                {
                    AutoScaleMode = AutoScaleMode.None, StartPosition = FormStartPosition.Manual, Location = new Point(12, 12),
                    FormBorderStyle = FormBorderStyle.None, ClientSize = new Size(760, 530), TopMost = true, ShowInTaskbar = false
                };
                using SettingsPage page = new(new()
                {
                    Style = theme, Appearance = mode,
                    AnimationsEnabled = false, TransparencyEnabled = true
                }, new(), (_, _) => null) { Dock = DockStyle.Fill, AutoScaleMode = AutoScaleMode.None };
                host.Controls.Add(page);
                host.Show(main);
                // Create collapsed descendants' HWNDs before injecting DPI. Handle creation otherwise restores monitor DPI.
                SettingsDisclosure disclosure = Find<SettingsDisclosure>(page, "scrollingOptionsButton");
                SettingsReveal reveal = Find<SettingsReveal>(page, "scrollingReveal");
                disclosure.PerformClick();
                reveal.Finish();
                disclosure.PerformClick();
                reveal.Finish();
                int monitorDpi = host.DeviceDpi;
                static IEnumerable<Control> Descendants(Control parent)
                {
                    yield return parent;
                    foreach (Control child in parent.Controls)
                    foreach (Control descendant in Descendants(child))
                        yield return descendant;
                }
                var controls = Descendants(host).Select(control => (Control: control, Font: control.Font)).ToArray();
                foreach (var entry in controls) deviceDpi.SetValue(entry.Control, dpi);
                page.Scale(new SizeF(scale, scale));
                host.ClientSize = new Size((int)(760 * scale), (int)(510 * scale));
                // GDI text still uses the physical monitor DPI. Adjust the fixture's point sizes to compensate.
                Dictionary<Font, Font> fonts = new();
                try
                {
                    foreach (var entry in controls)
                    {
                        if (!fonts.TryGetValue(entry.Font, out Font? scaled))
                        {
                            scaled = new Font(entry.Font.FontFamily, entry.Font.SizeInPoints * dpi / monitorDpi, entry.Font.Style);
                            fonts.Add(entry.Font, scaled);
                        }
                        entry.Control.Font = scaled;
                    }
                    page.RefreshSystemAppearance();
                    await Task.Delay(140);
                    Check(page.DeviceDpi == dpi, $"fixture renders at the requested {dpi} DPI (actual {page.DeviceDpi})");
                    Console.WriteLine($"DPI CHECK: simulated {dpi} DPI, {mode}");
                    if (theme != SoftwareStyle.Claude) VerifyAdditionalStyleSettings(page);
                    else VerifyAppearanceStates(page, page.PreviewPalette);
                    SaveFrame(Find<TableLayoutPanel>(page, "appearanceOptions"), Path.Combine(output, $"color-modes-{mode}-{dpi}.png"));
                    if (theme == SoftwareStyle.Claude) await VerifySettingsDividersAsync(page, page.PreviewPalette, output);
                }
                finally
                {
                    host.Close();
                    foreach (Font font in fonts.Values) font.Dispose();
                }
            }
        }
        finally { UiMotion.UserEnabled = originalMotion; }
    }

    private static void VerifyWorkspaceLayout(MainForm main)
    {
        Control toolbar = Find<Control>(main, "previewToolbar");
        PreviewSurface frame = Find<PreviewSurface>(main, "previewSurface");
        PreviewCanvas canvas = frame.Controls.OfType<PreviewCanvas>().Single();
        Check(toolbar.Bottom <= frame.Top && frame.Width == toolbar.Width && canvas.Width == frame.Width &&
            frame.ClientRectangle.Contains(canvas.Bounds), "the preview fills the space below the command bar");
    }

    private static void VerifyDescriptionFonts(Font interfaceFont)
    {
        LanguageMode previous = UiLanguage.Options.FirstOrDefault(option => option.CultureName == UiLanguage.Culture.Name)?.Mode
            ?? LanguageMode.System;
        try
        {
            foreach (LanguageMode language in new[] { LanguageMode.English, LanguageMode.SimplifiedChinese, LanguageMode.TraditionalChinese })
            {
                UiLanguage.Apply(language);
                using Font selected = CaptureTypography.CreateDescription(interfaceFont, 10, CapturePalette.ClaudeLight);
                Check(selected.Name != interfaceFont.Name && !selected.Bold,
                    $"{language} has a regular serif for descriptions ({selected.Name})");
            }
        }
        finally { UiLanguage.Apply(previous); }
    }

    internal static async Task RunAsync(MainForm main, SoftwareStyle theme = SoftwareStyle.Default)
    {
        string output = Path.GetFullPath(theme switch
        {
            SoftwareStyle.MetroClassic => $"artifacts/metro-style/{main.DeviceDpi}",
            SoftwareStyle.WinUI => $"artifacts/winui-style/{main.DeviceDpi}",
            SoftwareStyle.MaterialYou => $"artifacts/material-style/{main.DeviceDpi}",
            _ => "artifacts/software-style"
        });
        Directory.CreateDirectory(output);
        string scratch = Directory.CreateDirectory(Path.Combine(output, "preferences-" + Guid.NewGuid().ToString("N"))).FullName;
        string preferencesPath = Path.Combine(scratch, "preferences.json");
        string settingsPath = Path.Combine(scratch, "scrolling.json");
        try
        {
            if (theme is SoftwareStyle.MaterialYou or SoftwareStyle.WinUI or SoftwareStyle.MetroClassic)
                await VerifyAdditionalStyleAsync(main, output, preferencesPath, settingsPath, theme);
            else await VerifyAsync(main, output, preferencesPath, settingsPath);
        }
        finally
        {
            File.Delete(preferencesPath);
            File.Delete(settingsPath);
            Directory.Delete(scratch);
        }
    }

    private static void VerifyAdditionalStyleSettings(SettingsPage page)
    {
        CapturePalette palette = page.PreviewPalette;
        SettingsChoice style = Find<SettingsChoice>(page, "softwareStyleSelector");
        SettingsChoice language = Find<SettingsChoice>(page, "languageSelector");
        Check(style.Width == language.Width && style.Height == language.Height && style.Right == language.Right,
            $"{palette.Style} selectors align at {page.DeviceDpi} DPI");
        if (palette.IsMetro)
        {
            int available = style.Width - (int)Math.Ceiling((ButtonSpacing.Horizontal + 34) * style.DeviceDpi / 96f);
            int caption = TextRenderer.MeasureText(style.Text, style.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            Check(caption <= available, "the theme name fits in the selector");
        }
        CheckButtonFace(style, claude: true);
        ThemeOption[] cards = Find<Control>(page, "appearanceOptions").Controls.OfType<ThemeOption>().ToArray();
        Check(cards.Length == 3 && cards.Count(option => option.Checked) == 1, "color mode has one selection");
        foreach (ThemeOption option in cards)
        {
            option.SettleSelection();
            ((UiTween)typeof(ThemeOption).GetField("hover", Members)!.GetValue(option)!).SnapTo(0);
            using Bitmap bitmap = RenderControl(option);
            float scale = option.DeviceDpi / 96f;
            int y = bitmap.Height / 2 - (int)MathF.Round(25 * scale);
            Color expected = option.Checked ? palette.IsMetro ? palette.Accent : palette.Tint : palette.Surface;
            Color top = bitmap.GetPixel(bitmap.Width / 2, y);
            Check(top.ToArgb() == expected.ToArgb(),
                $"{option.Name} paints its tonal fill at {option.DeviceDpi} DPI");
            CheckButtonFace(option, claude: true);
        }
        Check(Find<MotionToggle>(page, "transparencyToggle").Enabled == palette.SupportsBackdrop,
            "settings show the correct backdrop control for the style");
    }

    private static async Task VerifyAdditionalStyleAsync(MainForm main, string output, string preferencesPath, string settingsPath, SoftwareStyle theme)
    {
        VerifyStyleContrast(theme);
        typeof(MainForm).GetField("preferencesPath", Members)!.SetValue(main, preferencesPath);
        typeof(MainForm).GetField("settingsPath", Members)!.SetValue(main, settingsPath);
        main.TopMost = true;
        main.ForceActivate();
        Button settings = Find<Button>(main, "settingsButton");
        Color startingBackground = main.BackColor;
        settings.PerformClick();
        SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
        SettingsChoice style = Find<SettingsChoice>(page, "softwareStyleSelector");
        Check(style.Items.Count == 5 && (int)SoftwareStyle.Default == 0 && (int)SoftwareStyle.Claude == 1 &&
            (int)SoftwareStyle.MaterialYou == 2 && (int)SoftwareStyle.WinUI == 3 && (int)SoftwareStyle.MetroClassic == 4,
            "the selector has five styles with their original preference IDs");
        style.SelectedIndex = (int)theme - 1;
        style.Focus();
        MethodInfo fieldKey = typeof(SettingsChoice).GetMethod("ProcessCmdKey", Members)!;
        MethodInfo menuKey = typeof(ToolStripDropDown).GetMethod("ProcessDialogKey", Members)!;
        ToolStripDropDownMenu OpenMenu()
        {
            fieldKey.Invoke(style, [default(Message), Keys.F4]);
            return (ToolStripDropDownMenu)typeof(SettingsChoice).GetField("menu", Members)!.GetValue(style)!;
        }
        ToolStripDropDownMenu menu = OpenMenu();
        menuKey.Invoke(menu, [Keys.Down]);
        menuKey.Invoke(menu, [Keys.Enter]);
        Check(style.SelectedIndex == (int)theme && style.Focused && menu.IsDisposed,
            $"keyboard selection previews {theme} and returns to a single focused field");
        Find<Button>(page, "cancelSettingsButton").PerformClick();
        Check(main.BackColor == startingBackground && !File.Exists(preferencesPath), "cancel returns to the previous style");

        foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
        {
            string shade = mode.ToString().ToLowerInvariant();
            if (mode == AppearanceMode.Dark) main.Size = main.MinimumSize;
            settings.PerformClick();
            style.SelectedIndex = (int)theme;
            Find<RadioButton>(page, "theme" + mode).Checked = true;
            CapturePalette palette = CapturePalette.For(mode, theme);
            await Task.Delay(280);
            Check(main.BackColor == palette.Background && page.BackColor == palette.Background, $"{theme} previews the selected color mode");
            VerifyAdditionalStyleSettings(page);
            MotionToggle transparency = Find<MotionToggle>(page, "transparencyToggle");
            if (palette.IsWinUI)
            {
                var backdrop = (WindowBackdrop)typeof(MainForm).GetField("windowBackdrop", Members)!.GetValue(main)!;
                transparency.Checked = true;
                Check(backdrop.IsActive == WindowBackdrop.IsSupported(), $"{theme} enables the native backdrop when available");
                transparency.Checked = false;
                Check(!backdrop.IsActive, $"{theme} uses the solid palette when transparency is off");
            }
            style.Focus();
            VerifyBorderPixels(style, palette.Accent, 1.5f);
            SaveFrame(main, Path.Combine(output, $"settings-{shade}.png"));
            menu = OpenMenu();
            Check(menu.Region != null && menu.Visible && menu.Region.IsVisible(0, 0) == palette.IsMetro,
                "the solid menu uses the style's popup geometry");
            SaveFrame(menu, Path.Combine(output, $"menu-{shade}.png"));
            menuKey.Invoke(menu, [Keys.Escape]);
            SettingsDisclosure disclosure = Find<SettingsDisclosure>(page, "scrollingOptionsButton");
            disclosure.PerformClick();
            SettingsReveal reveal = Find<SettingsReveal>(page, "scrollingReveal");
            reveal.Finish();
            Check(reveal.Height > 0 && Find<SettingsChoice>(reveal, "cbScrollMethod").BackColor == palette.Canvas,
                "the inline scrolling options use the active field colors");
            if (palette.IsWinUI)
                Check(reveal.Region is { } clip && !clip.IsVisible(reveal.Width / 2, reveal.Height - 1),
                    "the expanded scrolling fields leave the card's bottom edge visible");
            foreach (CaptureShortcut action in ApplicationPreferences.Actions) Find<Button>(page, action.Kind + "Clear").PerformClick();
            Find<Button>(page, "saveSettingsButton").PerformClick();
            ApplicationPreferences saved = ApplicationPreferences.Load(preferencesPath);
            Check(saved.Style == theme && saved.Appearance == mode, $"{theme} and its color mode survive reading the saved preferences");
            VerifyWorkspaceLayout(main);
            Check(Find<PreviewSurface>(main, "previewSurface").Palette == palette &&
                Find<Control>(main, "previewToolbar").BackColor == (palette.IsMaterial ? palette.Surface : palette.Background),
                "preview and toolbar use the active tonal surfaces");
            await Task.Delay(280);
            CaptureModeButton region = Find<CaptureModeButton>(main, "regionButton");
            UiTween hover = (UiTween)typeof(CaptureModeButton).GetField("hover", Members)!.GetValue(region)!;
            Color RegionFill(float amount)
            {
                hover.SnapTo(amount);
                using Bitmap bitmap = RenderControl(region);
                return bitmap.GetPixel(bitmap.Width / 2, (int)Math.Round(7 * region.DeviceDpi / 96f));
            }
            if (palette.IsMetro)
            {
                Check(RegionFill(1).ToArgb() != RegionFill(0).ToArgb() &&
                    RegionFill(0).ToArgb() != region.Parent!.BackColor.ToArgb(),
                    "the Metro capture tile is colored at rest and responds to hover");
            }
            else
            {
                Check(RegionFill(1).ToArgb() == palette.Hover.ToArgb() &&
                    RegionFill(0).ToArgb() == region.Parent!.BackColor.ToArgb(),
                    "the region screenshot button uses the toolbar surface at rest and keeps its hover feedback");
            }
            SaveFrame(main, Path.Combine(output, $"workspace-{shade}.png"));

            using ScrollingOptionsDialog options = new(new(), palette, transparent: false);
            options.Show(main);
            Check(options.BackColor == palette.Background && ((Button)options.AcceptButton!).BackColor == palette.Accent,
                "the scrolling dialog shares the panel colors and filled primary action");
            SaveFrame(options, Path.Combine(output, $"scrolling-options-{shade}.png"));
            options.Close();
        }
        settings.PerformClick();
        Check(style.SelectedIndex == (int)theme, "reopening settings shows the saved style");
        Find<RadioButton>(page, "themeSystem").Checked = true;
        Check(main.BackColor == CapturePalette.For(AppearanceMode.System, theme).Background,
            $"{theme} follows Windows light and dark mode when requested");
        Find<Button>(page, "resetSettingsButton").PerformClick();
        Check(style.SelectedIndex == (int)SoftwareStyle.Default && Find<MotionToggle>(page, "transparencyToggle").Enabled,
            "switching back restores default transparency controls");
    }

    private static void VerifyStyleContrast(SoftwareStyle theme)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        static double Luma(Color color) => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        static double Contrast(Color a, Color b)
        {
            double first = Luma(a) + .05, second = Luma(b) + .05;
            return Math.Max(first, second) / Math.Min(first, second);
        }

        double minimumText = double.MaxValue, minimumOutline = double.MaxValue;
        IEnumerable<CapturePalette> palettes = theme switch
        {
            SoftwareStyle.MetroClassic => [CapturePalette.MetroLight, CapturePalette.MetroDark],
            SoftwareStyle.WinUI => [CapturePalette.WinUILight, CapturePalette.WinUIDark],
            _ => from seed in new[] { Color.Black, Color.White, Color.Gray, Color.Red, Color.Yellow, Color.Lime,
                  Color.Cyan, Color.Blue, Color.Purple, Color.DeepPink, Color.SandyBrown, Color.Teal }
                 from dark in new[] { false, true }
                 select MaterialPalette.Create(seed, dark)
        };
        foreach (CapturePalette p in palettes)
        {
            Color pressed = p.PrimaryFill(1, 1);
            minimumText = Math.Min(minimumText, new[] { Contrast(p.Text, p.Background), Contrast(p.Secondary, p.RaisedSurface),
                Contrast(p.AccentText, p.Accent), Contrast(p.AccentText, pressed), Contrast(p.TintText, p.Tint) }.Min());
            minimumOutline = Math.Min(minimumOutline, Contrast(p.FieldBorder, p.RaisedSurface));
        }
        Check(minimumText >= 4.5 && minimumOutline >= 3, $"{theme} contrast is {minimumText:F2}:1 for text and {minimumOutline:F2}:1 for field outlines");
        if (theme == SoftwareStyle.MaterialYou)
            Check(MaterialPalette.Create(Color.Cyan, false) != MaterialPalette.Create(Color.Purple, false),
                "changing the Windows seed changes the generated palette");
    }

    private static async Task VerifyAsync(MainForm main, string output, string preferencesPath, string settingsPath)
    {
        File.WriteAllText(preferencesPath, """{"Appearance":2,"TransparencyEnabled":false}""");
        ApplicationPreferences legacy = ApplicationPreferences.Load(preferencesPath);
        Check(legacy.Style == SoftwareStyle.Default && legacy.Appearance == AppearanceMode.Dark && !legacy.TransparencyEnabled,
            "preferences from earlier builds retain their settings and default style");
        File.WriteAllText(preferencesPath, """{"Appearance":2,"Style":100}""");
        Check(ApplicationPreferences.Load(preferencesPath).Style == SoftwareStyle.Default, "unknown styles fall back to the default");
        File.WriteAllText(preferencesPath, """{"Appearance":2,"Style":5,"TransparencyEnabled":false,"AnimationsEnabled":false,"Shortcuts":{"region":0}}""");
        ApplicationPreferences retired = ApplicationPreferences.Load(preferencesPath);
        Check(retired.Style == SoftwareStyle.Default && retired.Appearance == AppearanceMode.Dark &&
            !retired.TransparencyEnabled && !retired.AnimationsEnabled && retired.Shortcuts["region"] == Keys.None,
            "the retired style ID falls back without losing other preferences");
        File.Delete(preferencesPath);
        typeof(MainForm).GetField("preferencesPath", Members)!.SetValue(main, preferencesPath);
        typeof(MainForm).GetField("settingsPath", Members)!.SetValue(main, settingsPath);

        Button settings = Find<Button>(main, "settingsButton");
        VerifyWorkspaceLayout(main);
        VerifyDescriptionFonts(main.Font);
        VerifyLinkArrow();
        Button zoomPercent = Find<Button>(main, "previewZoomPercent");
        string defaultZoomFont = zoomPercent.Font.Name;
        Color defaultBackground = main.BackColor;
        main.TopMost = true;
        main.ForceActivate();
        settings.PerformClick();
        SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
        string productVersion = typeof(MainForm).Assembly.GetName().Version!.ToString(3);
        Check(Find<Label>(page, "aboutVersion").Text == "Version " + productVersion,
            "the about page displays the application version " + productVersion);
        SettingsChoice style = Find<SettingsChoice>(page, "softwareStyleSelector");
        Button license = Find<Button>(page, "aboutLicense");
        FieldInfo transition = typeof(MainForm).GetField("pageTransition", Members)!;
        Check(style.SelectedIndex == 0, "the style selector initially shows the default");
        CheckButtonFace(license, claude: false);
        Find<RadioButton>(page, "themeLight").Checked = true;
        style.Focus();
        typeof(SettingsChoice).GetMethod("ProcessCmdKey", Members)!.Invoke(style, [default(Message), Keys.F4]);
        ToolStripDropDownMenu popup = (ToolStripDropDownMenu)typeof(SettingsChoice).GetField("menu", Members)!.GetValue(style)!;
        if (WindowBackdrop.IsSupported() && Find<MotionToggle>(page, "transparencyToggle").Checked)
            Check(popup.Region == null, "the style dropdown uses its native frosted surface");
        MethodInfo key = typeof(ToolStripDropDown).GetMethod("ProcessDialogKey", Members)!;
        key.Invoke(popup, [Keys.Down]);
        key.Invoke(popup, [Keys.Enter]);
        Check(style.SelectedIndex == (int)SoftwareStyle.Claude && style.Focused && !style.DroppedDown,
            "F4, Down and Enter choose Claude and return keyboard focus to the field");
        Check(main.BackColor == CapturePalette.ClaudeLight.Background && !Find<MotionToggle>(page, "transparencyToggle").Enabled,
            "style selection previews the light paper palette and pauses transparency");
        Check(transition.GetValue(main) == null, "style change presents the new live material without a snapshot overlay");
        VerifyFieldSurface(style);
        if (WindowBackdrop.IsSupported() && Find<MotionToggle>(page, "transparencyToggle").Checked)
        {
            typeof(SettingsChoice).GetMethod("ProcessCmdKey", Members)!.Invoke(style, [default(Message), Keys.F4]);
            ToolStripDropDownMenu claudePopup = (ToolStripDropDownMenu)typeof(SettingsChoice).GetField("menu", Members)!.GetValue(style)!;
            Check(claudePopup.Region == null, "the style dropdown remains frosted in Claude");
            key.Invoke(claudePopup, [Keys.Escape]);
        }
        CheckButtonFace(license, claude: true);
        CheckButtonFace(Find<RadioButton>(page, "themeLight"), claude: true);
        CheckButtonFace(style, claude: true);
        CheckButtonFace(Find<MotionToggle>(page, "animationsToggle"), claude: true);
        Label settingsTitle = Find<Label>(page, "settingsTitle");
        Label hint = Find<Label>(page, "transparencyHint");
        Check(settingsTitle.Font.Name == page.Font.Name && settingsTitle.Font.Bold && hint.Font.Name != page.Font.Name && !hint.Font.Bold,
            "settings titles are bold sans serif and descriptions are regular serif");

        typeof(MainForm).GetMethod("ApplyAppearance", Members)!.Invoke(main, null);
        Check(main.BackColor == CapturePalette.ClaudeLight.Background, "system refresh respects the active style draft");
        Find<Button>(page, "cancelSettingsButton").PerformClick();
        Check(!page.Visible && main.BackColor == defaultBackground
            && hint.Font.Name == page.Font.Name && !File.Exists(preferencesPath), "cancel restores the original style and type without writing preferences");
        Check(zoomPercent.Font.Name == defaultZoomFont, "cancel restores the default zoom readout font");
        Check(transition.GetValue(main) == null, "cancel across styles also avoids combining the two materials");

        CapturePalette[] palettes = [CapturePalette.ClaudeLight, CapturePalette.ClaudeDark];
        foreach (CapturePalette palette in palettes)
        {
            bool dark = palette == CapturePalette.ClaudeDark;
            main.Size = dark ? main.MinimumSize : new Size(1140 * main.DeviceDpi / 96, 820 * main.DeviceDpi / 96);
            settings.PerformClick();
            style.SelectedIndex = (int)SoftwareStyle.Claude;
            Find<RadioButton>(page, dark ? "themeDark" : "themeLight").Checked = true;
            await Task.Delay(270);
            Check(main.BackColor == palette.Background && page.BackColor == palette.Background &&
                style.Right <= style.Parent!.ClientSize.Width && style.Height >= 36 * page.DeviceDpi / 96,
                "style field and palette render at the " + (dark ? "minimum" : "wide") + " window size");
            MotionToggle animations = Find<MotionToggle>(page, "animationsToggle");
            Check(animations.Focus() && !style.Focused, "keyboard focus moves away from the style field");
            VerifyBorderPixels(style, palette.FieldBorder, 1);
            Check(style.Focus(), "keyboard focus returns to the style field");
            VerifyBorderPixels(style, palette.Accent, 1.5f);
            SaveFrame(main, Path.Combine(output, dark ? "settings-dark.png" : "settings-light.png"));
            VerifyAppearanceStates(page, palette);
            await VerifySettingsDividersAsync(page, palette, output);

            foreach (CaptureShortcut action in ApplicationPreferences.Actions)
                Find<Button>(page, action.Kind + "Clear").PerformClick();
            Button save = Find<Button>(page, "saveSettingsButton");
            ScrollableControl viewport = (ScrollableControl)typeof(SettingsPage).GetField("scroller", Members)!.GetValue(page)!;
            viewport.ScrollControlIntoView(save);
            save.PerformClick();
            await Task.Delay(270);
            ApplicationPreferences saved = ApplicationPreferences.Load(preferencesPath);
            Check(saved.Style == SoftwareStyle.Claude && saved.Appearance == (dark ? AppearanceMode.Dark : AppearanceMode.Light)
                && !page.Visible && Find<PreviewSurface>(main, "previewSurface").Palette == palette,
                $"save persists the style and applies the palette to the capture workspace (style={saved.Style}, mode={saved.Appearance}, settings visible={page.Visible}, feedback={Find<Label>(page, "settingsFeedback").Text})");
            VerifyWorkspaceLayout(main);
            CheckButtonFace(Find<Button>(main, "regionButton"), claude: true);
            CheckButtonFace(settings, claude: true);
            CheckButtonFace(zoomPercent, claude: true);
            Check(Find<Label>(page, "aboutAppName").Font.Name == page.Font.Name,
                "the brand heading uses the sans serif interface family");
            Check(!Find<Control>(main, "previewActions").Visible, "the empty toolbar hides image commands");
            PreviewCanvas canvas = Find<PreviewSurface>(main, "previewSurface").Controls.OfType<PreviewCanvas>().Single();
            using (Bitmap frame = new(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(frame, canvas.ClientRectangle);
                Check(canvas.Image == null && frame.GetPixel(frame.Width / 2, 8).ToArgb() == palette.Canvas.ToArgb(),
                    "the empty preview uses the palette's canvas color");
            }
            SaveFrame(main, Path.Combine(output, dark ? "workspace-dark.png" : "workspace-light.png"));
            using Font descriptionFont = CaptureTypography.CreateDescription(main.Font, 10, palette);
            Check(zoomPercent.Font.Name == descriptionFont.Name, "the zoom readout uses the serif description font");
            if (dark)
            {
                using Bitmap source = new(160, 120);
                typeof(MainForm).GetMethod("SetPreview", Members)!.Invoke(main, [(Bitmap)source.Clone()]);
                Check(zoomPercent.AccessibleDescription!.Contains("160") && zoomPercent.AccessibleDescription.Contains("120") &&
                    Find<Control>(main, "previewActions").Visible, "image actions and dimensions are available after a capture");
                using var editor = ScreenshotEditor.Create(source, palette, saved);
                editor.Show(main);
                await Task.Delay(140);
                Label editorTitle = Find<Label>(editor, "editorTitle");
                ToolStrip tools = Find<ToolStrip>(editor, "editorToolbar");
                Check(editorTitle.ForeColor == palette.Text && tools.BackColor == palette.Background,
                    "the editor uses the dark paper palette for its title and controls");
                ToolStripItem item = tools.Items.Cast<ToolStripItem>().First(item => item.Visible && item.GetCurrentParent() == tools);
                Rectangle face = item.Bounds;
                Check(tools.Region is { } toolRegion &&
                    toolRegion.IsVisible(face.X + face.Width / 2, face.Y + face.Height / 2) &&
                    !toolRegion.IsVisible(face.X + face.Width / 2, face.Top),
                    "the editor toolbar's native window excludes the gutter above tool buttons");
                Check(editorTitle.Font.Name == editor.Font.Name, "the editor title shares the interface font");
                Check(Find<Label>(editor, "editorStatus").Font.Name != editor.Font.Name,
                    "the editor's operating instructions use the serif description font");
                SaveFrame(editor, Path.Combine(output, "editor-dark.png"));
                editor.Close();
            }
        }

        settings.PerformClick();
        Check(style.SelectedIndex == (int)SoftwareStyle.Claude, "saved style is restored when settings reopen");
        Find<RadioButton>(page, "themeSystem").Checked = true;
        await Task.Delay(270);
        CapturePalette systemPalette = CapturePalette.For(AppearanceMode.System, SoftwareStyle.Claude);
        Check(main.BackColor == systemPalette.Background, "selecting System previews the Windows color mode");
        VerifyAppearanceStates(page, systemPalette);
        SaveFrame(Find<TableLayoutPanel>(page, "appearanceOptions"), Path.Combine(output, $"color-modes-{page.DeviceDpi}.png"));
        Find<Button>(page, "resetSettingsButton").PerformClick();
        Check(style.SelectedIndex == (int)SoftwareStyle.Default && Find<MotionToggle>(page, "transparencyToggle").Enabled,
            "reset previews the default style and re-enables the transparency control");
        if (Application.HighDpiMode != HighDpiMode.DpiUnaware) await VerifyMouseRoutingAsync(main);
    }

    private static void SaveFrame(Control control, string path)
    {
        using Bitmap image = RenderControl(control);
        image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static Bitmap RenderControl(Control control)
    {
        Bitmap image = new(control.Width, control.Height);
        control.DrawToBitmap(image, new Rectangle(Point.Empty, control.Size));
        return image;
    }
}
