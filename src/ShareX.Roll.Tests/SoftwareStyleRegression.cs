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
    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

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
        int ink = highlighted ? accent : CapturePalette.ClaudeLight.Text.ToArgb();
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

    private static Bitmap CaptureLiveControl(Control control)
    {
        Point start = control.PointToScreen(Point.Empty);
        Point end = control.PointToScreen(new Point(control.Width, control.Height));
        Check(LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref start) &&
            LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref end), "mapped " + control.Name + " to screen pixels");
        DwmFlush();
        return new Screenshot().CaptureRectangle(Rectangle.FromLTRB(start.X, start.Y, end.X, end.Y));
    }

    private static void VerifyFieldSurface(SettingsChoice style)
    {
        // The forced 96-DPI fixture virtualizes desktop coordinates.
        if (Application.HighDpiMode == HighDpiMode.DpiUnaware) return;
        using Bitmap frame = CaptureLiveControl(style);
        int y = frame.Height / 5;
        Color textSide = frame.GetPixel(frame.Width / 5, y);
        Color arrowSide = frame.GetPixel(frame.Width * 4 / 5, y);
        Check(textSide == arrowSide && (textSide.ToArgb() == CapturePalette.ClaudeLight.Canvas.ToArgb() ||
            textSide.ToArgb() == CapturePalette.ClaudeLight.Hover.ToArgb()),
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

    private static void VerifyWorkspaceLayout(MainForm main, int logicalSidebarWidth)
    {
        TableLayoutPanel body = Find<TableLayoutPanel>(main, "captureBody");
        int[] widths = body.GetColumnWidths();
        int expected = (int)Math.Round(logicalSidebarWidth * main.DeviceDpi / 96f);
        Check(Math.Abs(widths[0] - expected) <= 1 && widths[1] > widths[0],
            $"workspace sidebar scales once and leaves room for preview (DPI={main.DeviceDpi}, sidebar={widths[0]}, expected={expected}, preview={widths[1]})");
        PreviewSurface frame = Find<PreviewSurface>(main, "previewSurface");
        PreviewCanvas canvas = frame.Controls.OfType<PreviewCanvas>().Single();
        Check(body.ClientRectangle.Contains(frame.Bounds) && frame.ClientRectangle.Contains(canvas.Bounds),
            $"preview canvas stays in its panel (body={body.ClientSize}, panel={frame.Bounds}, canvas={canvas.Bounds})");
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

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/software-style");
        Directory.CreateDirectory(output);
        string scratch = Directory.CreateDirectory(Path.Combine(output, "preferences-" + Guid.NewGuid().ToString("N"))).FullName;
        string preferencesPath = Path.Combine(scratch, "preferences.json");
        string settingsPath = Path.Combine(scratch, "scrolling.json");
        try { await VerifyAsync(main, output, preferencesPath, settingsPath); }
        finally
        {
            File.Delete(preferencesPath);
            File.Delete(settingsPath);
            Directory.Delete(scratch);
        }
    }

    private static async Task VerifyAsync(MainForm main, string output, string preferencesPath, string settingsPath)
    {
        File.WriteAllText(preferencesPath, """{"Appearance":2,"TransparencyEnabled":false}""");
        ApplicationPreferences legacy = ApplicationPreferences.Load(preferencesPath);
        Check(legacy.Style == SoftwareStyle.Default && legacy.Appearance == AppearanceMode.Dark && !legacy.TransparencyEnabled,
            "preferences from earlier builds retain their settings and default style");
        File.WriteAllText(preferencesPath, """{"Appearance":2,"Style":100}""");
        Check(ApplicationPreferences.Load(preferencesPath).Style == SoftwareStyle.Default, "unknown styles fall back to the default");
        File.Delete(preferencesPath);
        typeof(MainForm).GetField("preferencesPath", Members)!.SetValue(main, preferencesPath);
        typeof(MainForm).GetField("settingsPath", Members)!.SetValue(main, settingsPath);

        Button settings = Find<Button>(main, "settingsButton");
        VerifyWorkspaceLayout(main, 272);
        VerifyDescriptionFonts(main.Font);
        VerifyLinkArrow();
        Label previewTitle = (Label)typeof(MainForm).GetField("previewTitle", Members)!.GetValue(main)!;
        Label previewHint = (Label)typeof(MainForm).GetField("previewHint", Members)!.GetValue(main)!;
        Button zoomPercent = Find<Button>(main, "previewZoomPercent");
        string defaultPreviewFont = previewHint.Font.Name;
        string defaultZoomFont = zoomPercent.Font.Name;
        string defaultTitleFamily = Find<Label>(main, "captureHeading").Font.Name;
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
        Font restoredTitle = Find<Label>(main, "captureHeading").Font;
        Check(!page.Visible && main.BackColor == defaultBackground && restoredTitle.Name == defaultTitleFamily && restoredTitle.Bold
            && hint.Font.Name == page.Font.Name && !File.Exists(preferencesPath), "cancel restores the original style and type without writing preferences");
        Check(previewHint.Font.Name == defaultPreviewFont && zoomPercent.Font.Name == defaultZoomFont,
            "cancel restores the default workspace fonts");
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
            style.Focus();
            VerifyBorderPixels(style, palette.Accent, 1.5f);
            VerifyBorderPixels(Find<RadioButton>(page, dark ? "themeDark" : "themeLight"), palette.Accent, 1);
            SaveFrame(main, Path.Combine(output, dark ? "settings-dark.png" : "settings-light.png"));

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
            VerifyWorkspaceLayout(main, 220);
            CheckButtonFace(Find<Button>(main, "regionButton"), claude: true);
            CheckButtonFace(settings, claude: true);
            CheckButtonFace(zoomPercent, claude: true);
            Check(previewTitle.Font.Name == main.Font.Name && previewTitle.Font.Bold,
                "the preview heading shares the interface family and uses weight for emphasis");
            Check(Find<Label>(main, "captureHeading").Font.Name == main.Font.Name &&
                Find<Label>(page, "aboutAppName").Font.Name == page.Font.Name,
                "workspace and brand headings share the sans serif interface family");
            Check(Find<Button>(main, "editButton").Top == Find<Button>(main, "saveButton").Top,
                "the preview commands fit on one row");
            SaveFrame(main, Path.Combine(output, dark ? "workspace-dark.png" : "workspace-light.png"));
            using Font descriptionFont = CaptureTypography.CreateDescription(main.Font, 10, palette);
            Check(previewHint.Font.Name == descriptionFont.Name && zoomPercent.Font.Name == descriptionFont.Name,
                "workspace preview status and zoom readout use the serif description font");
            foreach (CaptureShortcut action in ApplicationPreferences.Actions)
            {
                CaptureModeButton captureButton = Find<CaptureModeButton>(main, action.Kind + "Button");
                Font shortcutFont = (Font)typeof(CaptureModeButton).GetField("shortcutFont", Members)!.GetValue(captureButton)!;
                Check(shortcutFont.Name == descriptionFont.Name, action.Kind + " shortcut is drawn in the serif description font");
            }
            if (dark)
            {
                using Bitmap source = new(160, 120);
                typeof(MainForm).GetMethod("SetPreview", Members)!.Invoke(main, [(Bitmap)source.Clone()]);
                Check(previewHint.Text.Contains("160") && previewHint.Text.Contains("120") && previewHint.Font.Name == descriptionFont.Name,
                    "image dimensions keep the same serif font after a capture");
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
        Find<Button>(page, "resetSettingsButton").PerformClick();
        Check(style.SelectedIndex == (int)SoftwareStyle.Default && Find<MotionToggle>(page, "transparencyToggle").Enabled,
            "reset previews the default style and re-enables the transparency control");
        if (Application.HighDpiMode != HighDpiMode.DpiUnaware) await VerifyMouseRoutingAsync(main);
    }

    private static void SaveFrame(Control control, string path)
    {
        using Bitmap image = new(control.Width, control.Height);
        control.DrawToBitmap(image, new Rectangle(Point.Empty, control.Size));
        image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
