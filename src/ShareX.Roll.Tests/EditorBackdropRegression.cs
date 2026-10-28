using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class EditorBackdropRegression
{
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
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
