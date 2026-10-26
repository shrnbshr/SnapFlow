using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Collections;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

internal static class EditorEffectsRegression
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private delegate bool WindowCallback(nint window, nint data);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int length);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private static int checks;

    private sealed class BackgroundWindow : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { CreateParams value = base.CreateParams; value.ExStyle |= 0x08000000; return value; }
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        checks++;
        Console.WriteLine("PASS: " + message);
    }

    private static Rectangle PhysicalBounds(Control control)
    {
        Point start = control.PointToScreen(Point.Empty), end = control.PointToScreen(new Point(control.ClientSize));
        if (!LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref start) || !LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref end))
            throw new InvalidOperationException("Cannot map menu desktop coordinates");
        return Rectangle.FromLTRB(start.X, start.Y, end.X, end.Y);
    }

    private static int Distance(Color first, Color second) =>
        Math.Max(Math.Abs(first.R - second.R), Math.Max(Math.Abs(first.G - second.G), Math.Abs(first.B - second.B)));

    private static nint[] Shadows()
    {
        List<nint> windows = new();
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            StringBuilder name = new(128);
            GetWindowText(window, name, name.Capacity);
            if (name.ToString() == "SnapFlow.EditorMenuShadow" && IsWindowVisible(window)) windows.Add(window);
            return true;
        }, 0);
        return windows.ToArray();
    }

    private static void CheckShadows(int count, string context)
    {
        nint[] windows = Shadows();
        Console.WriteLine($"SHADOWS: {context}; expected={count}; actual={windows.Length}");
        Check(windows.Length == (SystemInformation.HighContrast ? 0 : count), context + ": each open menu owns one visible shadow and closed menus leave none");
        foreach (nint window in windows)
        {
            const long required = 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080;
            Check((GetWindowLongPtr(window, -20).ToInt64() & required) == required && window != GetForegroundWindow(),
                context + ": shadow is layered, click-through, absent from the taskbar and never activates");
        }
    }

    private static void VerifyButtonShadows(ToolStrip strip, string context)
    {
        EditorToolStripRenderer renderer = (EditorToolStripRenderer)strip.Renderer;
        IDictionary states = (IDictionary)renderer.GetType().GetField("states", Members)!.GetValue(renderer)!;
        IEnumerable<ToolStripItem> visible = strip.Items.Cast<ToolStripItem>().Where(item =>
            item.Available && item.Placement == ToolStripItemPlacement.Main && item is not ToolStripSeparator);
        if (strip.OverflowButton.Visible) visible = visible.Append(strip.OverflowButton);
        foreach (ToolStripItem item in visible)
        {
            object state = states[item]!;
            UiTween hover = (UiTween)state.GetType().GetField("Hover", Members)!.GetValue(state)!;
            UiTween press = (UiTween)state.GetType().GetField("Press", Members)!.GetValue(state)!;
            float oldHover = hover.Value, oldPress = press.Value;
            Rectangle bounds = item.Bounds;
            bool oldShadows = renderer.PaintingShadows;
            float dpi = strip.DeviceDpi / 96f;
            float height = Math.Min(40 * dpi, item.Height - 8 * dpi);
            RectangleF face = new(4 * dpi, (item.Height - height) / 2, item.Width - 8 * dpi, height);
            using GraphicsPath shape = CaptureDrawing.Rounded(face, 10 * dpi);
            Bitmap Render(bool shadow)
            {
                renderer.PaintingShadows = shadow;
                Bitmap result = new(item.Width, item.Height);
                using Graphics graphics = Graphics.FromImage(result);
                graphics.Clear(strip.BackColor);
                ToolStripItemRenderEventArgs args = new(graphics, item);
                if (item is ToolStripOverflowButton) renderer.DrawOverflowButtonBackground(args);
                else if (item is ToolStripDropDownButton) renderer.DrawDropDownButtonBackground(args);
                else renderer.DrawButtonBackground(args);
                return result;
            }
            long Energy()
            {
                using Bitmap shadow = Render(true), plain = Render(false);
                long difference = 0;
                for (int y = 0; y < shadow.Height; y++)
                for (int x = 0; x < shadow.Width; x++)
                    if (!shape.IsVisible(x + .5f, y + .5f)) difference += Distance(shadow.GetPixel(x, y), plain.GetPixel(x, y));
                return difference;
            }
            try
            {
                hover.SnapTo(0); press.SnapTo(0);
                long idle = Energy();
                hover.SnapTo(1);
                long raised = Energy();
                press.SnapTo(1);
                long pressed = Energy();
                Console.WriteLine($"BUTTON: {context}/{item.Name}; idle={idle}, hover={raised}, pressed={pressed}");
                Check(idle == 0, context + "/" + item.Name + ": idle button has no shadow");
                if (item.Enabled && !SystemInformation.HighContrast)
                    Check(raised > 0 && pressed < raised, context + "/" + item.Name + ": only hover adds a shadow outside the existing button face and pressing softens it");
                else
                    Check(raised == 0 && pressed == 0, context + "/" + item.Name + ": disabled and high-contrast buttons never gain a shadow");
                Check(item.Bounds == bounds, context + "/" + item.Name + ": shadow feedback preserves layout and hit target");
            }
            finally { renderer.PaintingShadows = oldShadows; hover.SnapTo(oldHover); press.SnapTo(oldPress); }
        }
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/editor-effects/" + main.DeviceDpi);
        Directory.CreateDirectory(output);
        bool motion = UiMotion.UserEnabled;
        Rectangle area = Screen.FromControl(main).WorkingArea;
        using BackgroundWindow background = new()
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Bounds = area
        };
        background.Show();
        try
        {
            UiMotion.UserEnabled = false;
            foreach (CapturePalette palette in new[] { CapturePalette.Light, CapturePalette.Dark })
            foreach (bool transparent in new[] { true, false })
            {
                palette.ApplyLibraryTheme();
                string theme = palette == CapturePalette.Light ? "light" : "dark";
                using Bitmap image = new(600, 360);
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(palette.Background);
                    graphics.FillRectangle(Brushes.Black, 200, 0, 200, 360);
                    graphics.FillRectangle(Brushes.White, 400, 0, 200, 360);
                }
                using RegionCaptureForm editor = ScreenshotEditor.Create(image, palette, new() { TransparencyEnabled = transparent });
                editor.TopMost = true;
                editor.Location = new(area.Left + 16, area.Top + 16);
                editor.Width = Math.Min(area.Width / 2, 660 * editor.DeviceDpi / 96);
                editor.Show(main);
                await Task.Delay(250);
                // Keep its owner buttons on-screen while native SetWindowPos moves the popup sample.
                editor.Location = new(area.Left + 16, area.Top + 16);
                editor.ForceActivate();
                await Task.Delay(150);
                Console.WriteLine($"LAYOUT: desktop={area}; editor={editor.Bounds}; dpi={editor.DeviceDpi}");
                ToolStrip tools = EditorRegression.Tools(editor), actions = EditorRegression.Actions(editor);
                VerifyButtonShadows(actions, theme + "-actions");
                VerifyButtonShadows(tools, theme + "-crop-tools");
                tools.Items["editorCancelCrop"]!.PerformClick();
                VerifyButtonShadows(tools, theme + "-annotation-tools");
                CheckShadows(0, theme + " before menus");

                async Task VerifyMenu(ToolStripDropDown menu, string kind, int shadows)
                {
                    menu.Closing += (_, args) => Console.WriteLine($"MENU-CLOSE: {kind}; reason={args.CloseReason}; foreground={GetForegroundWindow()}; editor={editor.Handle}");
                    Console.WriteLine($"FOCUS: {kind}; foreground={GetForegroundWindow()}; editor={editor.Handle}");
                    Console.WriteLine($"MENU-STYLE: {kind}; style={GetWindowLongPtr(menu.Handle, -16).ToInt64():X}; exstyle={GetWindowLongPtr(menu.Handle, -20).ToInt64():X}");
                    Check(menu.Visible, kind + ": original native menu opens");
                    await Task.Delay(250);
                    Check(menu.Visible, kind + ": original native menu stays open before controlled placement");
                    // The normal activation behavior is checked above. Hold the relocated sample open
                    // while repainting two desktop backgrounds; unrelated foreground changes must not
                    // replace the material sample with bare desktop pixels.
                    menu.AutoClose = false;
                    int inset = Math.Max(16, (int)Math.Round(24 * menu.DeviceDpi / 96f));
                    Check(SetWindowPos(menu.Handle, 0, area.Right - menu.Width - inset, area.Top + inset, 0, 0, 0x215),
                        kind + ": native popup can be positioned over the controlled desktop");
                    Console.WriteLine($"LAYOUT: {kind}; desktop={area}; editor={editor.Bounds}; menu={menu.Bounds}; dpi={menu.DeviceDpi}");
                    Check(!editor.Bounds.IntersectsWith(menu.Bounds), kind + ": controlled menu sampling is outside the screenshot editor");
                    await Task.Delay(150);
                    Check(menu.Visible, kind + ": native popup stays open after positioning");
                    bool active = transparent && WindowBackdrop.IsSupported();
                    if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
                        Check(DwmGetWindowAttribute(menu.Handle, 38, out int material, sizeof(int)) >= 0 && material == 1,
                            kind + ": menu material follows transparency=" + transparent);
                    CheckShadows(shadows, kind);
                    Rectangle menuPixels = PhysicalBounds(menu);
                    Rectangle capture = menuPixels;
                    int halo = (int)Math.Ceiling(24 * menu.DeviceDpi / 96f);
                    capture.Inflate(halo, halo);
                    async Task<Bitmap> Capture(Color color)
                    {
                        background.BackColor = color;
                        background.Refresh();
                        Check(SetWindowPos(background.Handle, editor.Handle, 0, 0, 0, 0, 0x213), kind + ": controlled background stays behind the editor and menus");
                        menu.Refresh();
                        await Task.Delay(300);
                        DwmFlush();
                        return new Screenshot { RemoveOutsideScreenArea = false }.CaptureRectangle(capture);
                    }
                    using Bitmap red = await Capture(Color.Red), blue = await Capture(Color.Blue);
                    red.Save(Path.Combine(output, $"{theme}-{transparent}-{kind}-red.png"), ImageFormat.Png);
                    blue.Save(Path.Combine(output, $"{theme}-{transparent}-{kind}-blue.png"), ImageFormat.Png);
                    int y = halo + Math.Max(2, (int)Math.Round(3 * menu.DeviceDpi / 96f));
                    int x = halo + menuPixels.Width / 2;
                    int response = Distance(red.GetPixel(x, y), blue.GetPixel(x, y));
                    Console.WriteLine($"MENU: {theme}/{transparent}/{kind}; active={active}, response={response}; red={red.GetPixel(x, y)}, blue={blue.GetPixel(x, y)}");
                    Check(active ? response >= 2 : response <= 3, kind + ": live menu background responds to the desktop only when transparency is active");
                    if (!SystemInformation.HighContrast)
                    {
                        int strongest = 0;
                        for (int offset = 1; offset <= Math.Min(halo - 1, 12 * menu.DeviceDpi / 96); offset++)
                            strongest = Math.Max(strongest, Distance(blue.GetPixel(halo + menuPixels.Width + offset, halo + menuPixels.Height / 2), Color.Blue));
                        Check(strongest >= 2, kind + ": composed desktop contains a soft shadow outside the rounded menu");
                    }
                    using Bitmap plain = new(menu.Width, menu.Height);
                    menu.DrawToBitmap(plain, new Rectangle(Point.Empty, plain.Size));
                    Check(plain.GetPixel(menu.Width / 2, Math.Max(2, 3 * menu.DeviceDpi / 96)).ToArgb() == palette.Background.ToArgb(),
                        kind + ": ordinary capture keeps a solid fallback background");
                }

                ToolStripDropDownItem colors = (ToolStripDropDownItem)tools.Items["editorAnnotationColor"]!;
                ToolStripDropDown colorMenu = colors.DropDown;
                colors.ShowDropDown();
                await VerifyMenu(colorMenu, "colors", 1);
                colorMenu.AutoClose = true;
                typeof(ToolStripDropDown).GetMethod("ProcessDialogKey", Members)!.Invoke(colorMenu, [Keys.Menu]);
                await Task.Delay(30);
                CheckShadows(0, "closed colors");

                tools.OverflowButton.ShowDropDown();
                ToolStripDropDown more = tools.OverflowButton.DropDown;
                await VerifyMenu(more, "more", 1);
                // Keep the parent at the left edge so the submenu samples the controlled desktop directly.
                Check(SetWindowPos(more.Handle, 0, area.Left + 16, area.Top + (int)Math.Round(160 * editor.DeviceDpi / 96f), 0, 0, 0x215),
                    "parent menu can be positioned separately from its submenu");
                ToolStripDropDownItem edit = (ToolStripDropDownItem)tools.Items["editorEditMenu"]!;
                ToolStripDropDown editMenu = edit.DropDown;
                edit.ShowDropDown();
                await VerifyMenu(editMenu, "edit-submenu", 2);
                Check(!more.Bounds.IntersectsWith(editMenu.Bounds), "nested menu sample is independent of the parent material");
                editMenu.AutoClose = true;
                edit.HideDropDown();
                await Task.Delay(30);
                CheckShadows(1, "closed edit submenu");
                more.AutoClose = true;
                typeof(ToolStripDropDown).GetMethod("ProcessDialogKey", Members)!.Invoke(more, [Keys.Menu]);
                await Task.Delay(30);
                CheckShadows(0, "closed overflow");
                using (Bitmap result = editor.GetResultImage())
                    Check(result.Size == image.Size && Enumerable.Range(0, 3).All(i => result.GetPixel(100 + 200 * i, 180).ToArgb() == image.GetPixel(100 + 200 * i, 180).ToArgb()),
                        "menu materials and shadows preserve screenshot dimensions and black, white and page-background source pixels");
                typeof(RegionCaptureForm).GetMethod("CloseWindow", Members)!.Invoke(editor, [RegionResult.Close]);
                editor.Dispose();
                await Task.Delay(30);
                CheckShadows(0, "closed editor");
                Check(colorMenu.IsDisposed && more.IsDisposed && editMenu.IsDisposed,
                    "disposing the editor releases custom color, overflow and nested popups with their native material observers");
            }
        }
        finally { UiMotion.UserEnabled = motion; CapturePalette.Current.ApplyLibraryTheme(); }
        Console.WriteLine($"Editor effects: {checks} checks passed.");
    }
}
