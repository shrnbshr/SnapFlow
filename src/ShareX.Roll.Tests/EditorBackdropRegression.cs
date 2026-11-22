using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

internal static class EditorBackdropRegression
{
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private delegate bool WindowCallback(nint window, nint data);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeBounds bounds);
    [StructLayout(LayoutKind.Sequential)] private struct NativeBounds { public int Left, Top, Right, Bottom; }
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        checks++;
        Console.WriteLine("PASS: " + message);
    }

    private sealed class BackgroundWindow : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { CreateParams value = base.CreateParams; value.ExStyle |= 0x08000000; return value; }
        }
    }

    private static Point PhysicalPoint(Control control, Point point)
    {
        point = control.PointToScreen(point);
        if (!LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref point)) throw new InvalidOperationException("Cannot map desktop coordinates");
        return point;
    }
    private static Rectangle PhysicalBounds(Control control)
    {
        Point start = PhysicalPoint(control, Point.Empty), end = PhysicalPoint(control, new Point(control.ClientSize));
        return Rectangle.FromLTRB(start.X, start.Y, end.X, end.Y);
    }
    private static Color Sample(Bitmap frame, Form editor, Control control, Point point)
    {
        Point origin = PhysicalPoint(editor, Point.Empty), pixel = PhysicalPoint(control, point);
        return frame.GetPixel(pixel.X - origin.X, pixel.Y - origin.Y);
    }
    private static int Distance(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    private static Bitmap CaptureDesktop(Rectangle bounds)
    {
        nint previous = SetThreadDpiAwarenessContext(-4);
        try
        {
            DwmFlush();
            return new Screenshot { RemoveOutsideScreenArea = false }.CaptureRectangle(bounds);
        }
        finally { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
    }

    private static double Contrast(Color first, Color second)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        static double Light(Color color) => .05 + .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        double a = Light(first), b = Light(second);
        return Math.Max(a, b) / Math.Min(a, b);
    }

    internal static async Task RunMaterialsAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/editor-materials/" + main.DeviceDpi);
        Directory.CreateDirectory(output);
        VerifyMaterialContrast();

        bool motion = UiMotion.UserEnabled;
        Point cursor = Cursor.Position;
        using BackgroundWindow background = new()
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, BackColor = Color.Black,
            StartPosition = FormStartPosition.Manual, Bounds = Screen.FromControl(main).WorkingArea, TopMost = true
        };
        background.Show();
        try
        {
            UiMotion.UserEnabled = true;
            foreach ((CapturePalette palette, bool transparent) in new[]
            {
                (CapturePalette.Light, true), (CapturePalette.Dark, true),
                (CapturePalette.WinUILight, true), (CapturePalette.WinUIDark, true)
            })
            {
                palette.ApplyLibraryTheme();
                using Bitmap image = new(600, 360);
                using (Graphics graphics = Graphics.FromImage(image)) graphics.Clear(Color.DarkSlateGray);
                using RegionCaptureForm editor = ScreenshotEditor.Create(image, palette, new() { TransparencyEnabled = transparent });
                editor.TopMost = true;
                editor.Show(main);
                editor.ForceActivate();
                ToolStrip tools = EditorRegression.Tools(editor);
                tools.Items["editorCancelCrop"]!.PerformClick();
                tools.PerformLayout();
                await Task.Delay(200);
                background.BackColor = Color.Black;
                background.Refresh();
                Check(SetWindowPos(background.Handle, editor.Handle, 0, 0, 0, 0, 0x213), "controlled black backdrop is behind the editor");
                string variant = $"{palette.Style}-{(palette.Background.GetBrightness() < .5f ? "dark" : "light")}-{transparent}";
                await VerifyMenuAsync(editor, tools, background, palette, transparent, output, variant);
                typeof(RegionCaptureForm).GetMethod("CloseWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [RegionResult.Close]);
            }
        }
        finally
        {
            Cursor.Position = cursor;
            UiMotion.UserEnabled = motion;
            CapturePalette.Current.ApplyLibraryTheme();
        }
    }

    private static void VerifyMaterialContrast()
    {
        foreach (bool dark in new[] { false, true })
        {
            (CapturePalette Palette, uint Window, uint Menu, uint Preview, int PreviousSecondary)[] styles = dark
                ? [(CapturePalette.WinUIDark, 31, 48, 33, 0xCDCDCD), (CapturePalette.Dark, 31, 48, 33, 0xB3B3BC)]
                : [(CapturePalette.WinUILight, 22, 22, 30, 0x545454), (CapturePalette.Light, 22, 22, 30, 0x62626A)];
            uint lastWindow = 0, lastMenu = 0, lastPreview = 0;
            Color backdrop = dark ? Color.White : Color.Black;
            foreach (var style in styles)
            {
                CapturePalette palette = style.Palette;
                uint window = BackdropSnapshot.RemovedMatte(palette) >> 24;
                uint menu = BackdropSnapshot.RemovedMenuMatte(palette) >> 24;
                uint preview = (uint)(255 - BackdropSnapshot.PreviewTint(palette).A);
                Check(window > Math.Max(style.Window, lastWindow) && menu > Math.Max(style.Menu, lastMenu) &&
                      preview > Math.Max(style.Preview, lastPreview),
                    $"{palette.Style}: {(dark ? "dark" : "light")} material transmits more than before and stays distinct");
                lastWindow = window;
                lastMenu = menu;
                lastPreview = preview;
                Color previousSecondary = Color.FromArgb(style.PreviousSecondary | unchecked((int)0xFF000000));
                foreach ((Color surface, uint current, uint previous, string name) in new[]
                {
                    (palette.Background, window, style.Window, "window"),
                    (palette.MenuSurface, menu, style.Menu, "menu"),
                    (palette.Surface, preview, style.Preview, "zoom toolbar")
                })
                {
                    Color composited = CaptureDrawing.Blend(surface, backdrop, current / 255f);
                    Color old = CaptureDrawing.Blend(surface, backdrop, previous / 255f);
                    double secondary = Contrast(palette.Secondary, composited);
                    Check(secondary >= Math.Max(4.5, Contrast(previousSecondary, old)) &&
                        Contrast(palette.Text, composited) >= 5.5 && Contrast(palette.LinkInk, composited) >= 4.5,
                        $"{palette.Style}: {name} secondary contrast is {secondary:F2}:1 against the worst-case backdrop");
                }
            }
        }
    }

    private static async Task VerifyMenuAsync(Form editor, ToolStrip tools, Form background, CapturePalette palette,
        bool transparent, string output, string variant)
    {
        ToolStripOverflowButton more = tools.OverflowButton;
        more.ShowDropDown();
        ToolStripDropDown overflow = more.DropDown;
        ToolStripDropDownItem edit = (ToolStripDropDownItem)tools.Items["editorEditMenu"]!;
        edit.ShowDropDown();
        ToolStripDropDown submenu = edit.DropDown;
        await Task.Delay(175);
        Check(overflow.Visible && submenu.Visible, variant + ": overflow opens the native edit submenu");
        ToolStripItem undo = submenu.Items["editorUndo"]!, paste = submenu.Items["editorPaste"]!;
        undo.Enabled = false;
        paste.Enabled = true;
        submenu.Refresh();
        Rectangle bounds = PhysicalBounds(submenu);
        using Bitmap frame = CaptureDesktop(bounds);
        frame.Save(Path.Combine(output, variant + "-submenu.png"));
        foreach (ToolStripItem item in new[] { undo, paste })
        {
            int dpi = submenu.DeviceDpi;
            Color ink = item.Enabled ? palette.Text : palette.Secondary;
            Point Local(Point point) => PhysicalPoint(submenu, point) - (Size)bounds.Location;
            Point sample = Local(new Point(item.Bounds.Left + Math.Max(3, 5 * dpi / 96), item.Bounds.Top + item.Height / 2));
            Color surface = frame.GetPixel(sample.X, sample.Y);
            foreach (bool shortcut in new[] { false, true })
            {
                Rectangle text = EditorMenuLayout.TextBounds(item, item.Font, dpi, shortcut);
                text.Offset(item.Bounds.Location);
                Point start = Local(text.Location), end = Local(new Point(text.Right, text.Bottom));
                Color closest = surface;
                for (int y = start.Y; y < end.Y; y++)
                for (int x = start.X; x < end.X; x++)
                {
                    Color pixel = frame.GetPixel(x, y);
                    if (Distance(pixel, ink) < Distance(closest, ink)) closest = pixel;
                }
                Check(Distance(closest, ink) <= 4 && Contrast(closest, surface) >= 4.5,
                    $"{variant}: {item.Name} {(shortcut ? "shortcut" : "label")} contrast is {Contrast(closest, surface):F2}:1 over a dark backdrop");
            }
        }
        if (transparent && WindowBackdrop.IsSupported())
        {
            // The menu must still respond to what is behind it after strengthening the tint.
            Rectangle area = Rectangle.Union(PhysicalBounds(editor), Rectangle.Union(PhysicalBounds(overflow), bounds));
            using Bitmap context = CaptureDesktop(area);
            context.Save(Path.Combine(output, variant + "-editor-and-popups.png"));
            background.BackColor = Color.White;
            background.Refresh();
            await Task.Delay(250);
            using Bitmap bright = CaptureDesktop(bounds);
            int difference = 0;
            for (int y = bounds.Height / 4; y < bounds.Height * 3 / 4; y += 5)
            for (int x = bounds.Width / 4; x < bounds.Width * 3 / 4; x += 5)
                difference = Math.Max(difference, Distance(bright.GetPixel(x, y), frame.GetPixel(x, y)));
            Check(difference >= 2, $"{variant}: the clipped menu remains translucent (background response {difference})");
        }
        submenu.Close();
        overflow.Close();
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/editor-backdrop/" + main.DeviceDpi);
        Directory.CreateDirectory(output);
        bool motion = UiMotion.UserEnabled;
        Point originalCursor = Cursor.Position;
        using BackgroundWindow background = new()
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Bounds = Screen.FromControl(main).WorkingArea
        };
        background.Show();
        try
        {
            UiMotion.UserEnabled = false;
            foreach (CapturePalette palette in new[] { CapturePalette.Light, CapturePalette.Dark })
            foreach (bool transparent in new[] { false, true })
            {
                palette.ApplyLibraryTheme();
                using Bitmap image = new(600, 360);
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.Clear(palette.Background);
                    g.FillRectangle(Brushes.Black, 200, 0, 200, 360);
                    g.FillRectangle(Brushes.White, 400, 0, 200, 360);
                }
                ApplicationPreferences preferences = new() { TransparencyEnabled = transparent };
                using RegionCaptureForm editor = ScreenshotEditor.Create(image, palette, preferences);
                editor.TopMost = true; // Desktop sampling must not read another foreground window's pixels.
                editor.Show(main);
                await Task.Delay(350);
                if (NativeMethods.GetForegroundWindow() != editor.Handle)
                {
                    Point title = editor.PointToScreen(new Point(editor.ClientSize.Width / 2, -SystemInformation.CaptionHeight / 2));
                    Check(GetAncestor(WindowFromPoint(title), 2) == editor.Handle, "activation click targets the test editor title bar");
                    Check(InputHelpers.SendMouseClick(title), "physical title-bar click activates the test editor");
                    await Task.Delay(100);
                }
                Check(NativeMethods.GetForegroundWindow() == editor.Handle, "material sampling uses a foreground editor");
                ToolStrip actions = EditorRegression.Actions(editor);
                Form toolbar = actions.FindForm()!;
                bool active = transparent && WindowBackdrop.IsSupported();
                Check(!toolbar.TopLevel && toolbar.Parent == editor, "toolbar and canvas share one window material and activation context");
                foreach (Form window in new[] { editor })
                {
                    Check(window.Opacity == 1 && window.TransparencyKey.IsEmpty, "editor material preserves native window opacity and hit testing");
                    if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
                        Check(DwmGetWindowAttribute(window.Handle, 38, out int material, sizeof(int)) >= 0 && material == (active ? 3 : 1),
                            "canvas and toolbar native materials follow transparency=" + transparent);
                }
                string theme = palette == CapturePalette.Light ? "light" : "dark";
                async Task<Bitmap> Capture(Color color)
                {
                    background.BackColor = color;
                    background.Refresh();
                    Check(SetWindowPos(background.Handle, editor.Handle, 0, 0, 0, 0, 0x213), "controlled background stays behind the editor");
                    editor.ForceActivate();
                    editor.Refresh();
                    toolbar.Refresh();
                    await Task.Delay(700);
                    DwmFlush();
                    return new Screenshot { RemoveOutsideScreenArea = false }.CaptureRectangle(PhysicalBounds(editor));
                }
                using Bitmap red = await Capture(Color.Red);
                using Bitmap blue = await Capture(Color.Blue);
                red.Save(Path.Combine(output, $"{theme}-{transparent}-red.png"), ImageFormat.Png);
                blue.Save(Path.Combine(output, $"{theme}-{transparent}-blue.png"), ImageFormat.Png);
                foreach ((Control control, Point point, string name) in new[]
                {
                    ((Control)editor, new Point(8 * editor.DeviceDpi / 96, editor.ClientSize.Height - 24 * editor.DeviceDpi / 96), "canvas surround"),
                    ((Control)toolbar, new Point(12 * editor.DeviceDpi / 96, 58 * editor.DeviceDpi / 96), "toolbar")
                })
                {
                    int difference = Distance(Sample(red, editor, control, point), Sample(blue, editor, control, point));
                    Console.WriteLine($"MATERIAL: {theme}, enabled={transparent}, active={active}, {name}, background response={difference}");
                    Console.WriteLine($"SAMPLE: red={Sample(red, editor, control, point)}, blue={Sample(blue, editor, control, point)}");
                    Check(active ? difference >= 2 : difference <= 3, name + ": native background response agrees with the transparency setting");
                }
                RectangleF canvas = editor.EditorImageBounds;
                Color[] expected = [palette.Background, Color.Black, Color.White];
                for (int i = 0; i < expected.Length; i++)
                {
                    Point point = Point.Round(new PointF(canvas.Left + canvas.Width * (i + .5f) / 3, canvas.Top + canvas.Height / 2));
                    Color first = Sample(red, editor, editor, point), second = Sample(blue, editor, editor, point);
                    Check(Distance(first, second) <= 2 && Distance(first, expected[i]) <= 3,
                        "image pixels remain opaque, including black and the exact page background color");
                }
                using (Bitmap result = editor.GetResultImage())
                    Check(result.Size == image.Size && Enumerable.Range(0, 3).All(i => result.GetPixel(100 + 200 * i, 180).ToArgb() == expected[i].ToArgb()),
                        "export keeps the original dimensions and source colors");
                typeof(RegionCaptureForm).GetMethod("CloseWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [RegionResult.Close]);
            }
        }
        finally { UiMotion.UserEnabled = motion; Cursor.Position = originalCursor; CapturePalette.Current.ApplyLibraryTheme(); }
        Console.WriteLine($"Editor backdrop: {checks} checks passed.");
    }
}
