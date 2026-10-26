using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class ThemeTransitionRegression
{
    private sealed class FloatingWindow : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { CreateParams value = base.CreateParams; value.ExStyle |= 0x08000000; return value; }
        }
    }
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Call(MainForm main, string name) => typeof(MainForm).GetMethod(name, Private)!.Invoke(main, null);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPhysicalPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    private static nint PhysicalRoot(Point point)
    {
        nint previous = SetThreadDpiAwarenessContext(-4);
        try { return GetAncestor(WindowFromPhysicalPoint(point), 2); }
        finally { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
    }
    private static Bitmap Render(Control control)
    {
        Bitmap frame = new(control.Width, control.Height);
        control.DrawToBitmap(frame, control.ClientRectangle);
        return frame;
    }
    private static Bitmap Screen(Control control)
    {
        Point first = control.PointToScreen(Point.Empty), last = control.PointToScreen(new Point(control.Width, control.Height));
        LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref first); LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref last);
        using Bitmap physical = new Screenshot { RemoveOutsideScreenArea = false }
            .CaptureRectangle(Rectangle.FromLTRB(first.X, first.Y, last.X, last.Y));
        return physical.Size == control.Size ? (Bitmap)physical.Clone() : new Bitmap(physical, control.Size);
    }
    private static double Difference(Bitmap first, Bitmap last, int tolerance = 6)
    {
        int different = 0, total = 0;
        for (int y = 8; y < first.Height - 8; y += 4)
        for (int x = 8; x < first.Width - 8; x += 4)
        {
            Color a = first.GetPixel(x, y), b = last.GetPixel(x, y);
            if (Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))) > tolerance) different++;
            total++;
        }
        return (double)different / total;
    }
    internal static async Task RunAsync(MainForm main)
    {
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        bool topmost = main.TopMost; Point cursor = Cursor.Position;
        string output = Path.GetFullPath("artifacts/settings-response/theme-" + main.DeviceDpi);
        Directory.CreateDirectory(output);
        try
        {
            main.TopMost = true; main.ForceActivate(); Cursor.Position = Point.Empty;
            var working = original.Clone(); working.AnimationsEnabled = true; working.Appearance = AppearanceMode.Light;
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, working);
            Call(main, "ApplyAppearance"); Call(main, "OpenSettings"); await Task.Delay(450);
            SettingsPage page = Field<SettingsPage>(main, "settingsPage");
            if (!UiMotion.Enabled) throw new InvalidOperationException("Theme transition verification requires enabled Windows animations.");
            foreach (AppearanceMode target in new[] { AppearanceMode.Dark, AppearanceMode.Light })
            {
                main.ForceActivate();
                if (NativeMethods.GetForegroundWindow() != main.Handle)
                    InputHelpers.SendMouseClick(main.PointToScreen(new Point(8, 8)));
                await Task.Delay(350);
                Check(NativeMethods.GetForegroundWindow() == main.Handle, target + ": the test owns the foreground before comparing displayed pixels");
                List<Point> visible = new();
                for (int y = 8; y < page.Height - 8; y += 8)
                for (int x = 8; x < page.Width - 8; x += 8)
                {
                    Point point = page.PointToScreen(new Point(x, y));
                    LogicalToPhysicalPointForPerMonitorDPI(page.Handle, ref point);
                    if (PhysicalRoot(point) == main.Handle) visible.Add(new Point(x, y));
                }
                using Bitmap before = Screen(page);
                ((RadioButton)page.Controls.Find("theme" + target, true).Single()).Checked = true;
                PageTransition transition = Field<PageTransition>(main, "pageTransition");
                Check(transition.IsAnimating && transition.MaterialPlacement.BeforeX == 0 && transition.MaterialPlacement.AfterX == 0,
                    target + ": theme crossfade starts without moving the content");
                UiTween progress = Field<UiTween>(transition, "progress");
                progress.SnapTo(0); using Bitmap first = Render(transition);
                before.Save(Path.Combine(output, target + "-before-debug.png"), ImageFormat.Png);
                first.Save(Path.Combine(output, target + "-first-debug.png"), ImageFormat.Png);
                Console.WriteLine($"FRAME {target}: visible={visible.Count}, before={before.GetPixel(8, 8)}, first={first.GetPixel(8, 8)}, handle={main.Handle}");
                double difference = (double)visible.Count(point =>
                {
                    Color a = first.GetPixel(point.X, point.Y), b = before.GetPixel(point.X, point.Y);
                    return Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))) > 6;
                }) / visible.Count;
                Check(difference < .01, $"{target}: first frame preserves the previous desktop appearance ({difference:P3})");
                progress.SnapTo(.5f); using Bitmap middle = Render(transition);
                progress.SnapTo(1); using Bitmap last = Render(transition);
                using Bitmap expected = Render(page);
                Check(Difference(middle, first) > .25 && Difference(middle, last) > .25,
                    target + ": a visible intermediate frame blends the two themes");
                Check(Difference(last, expected) < .005, target + ": the final frame matches the live target theme");
                middle.Save(Path.Combine(output, target + "-middle.png"), ImageFormat.Png);
                Call(main, "EndPageTransition"); main.Refresh(); await Task.Delay(180);
            }
            ((RadioButton)page.Controls.Find("themeDark", true).Single()).Checked = true;
            Field<UiTween>(Field<PageTransition>(main, "pageTransition"), "progress").SnapTo(.4f);
            main.Refresh();
            using Bitmap interrupted = Screen(page);
            ((RadioButton)page.Controls.Find("themeLight", true).Single()).Checked = true;
            PageTransition reversed = Field<PageTransition>(main, "pageTransition");
            Field<UiTween>(reversed, "progress").SnapTo(0);
            using Bitmap resumed = Render(reversed);
            Check(Difference(interrupted, resumed) < .01, "rapid theme reversal starts at the currently presented appearance");
            Call(main, "EndPageTransition");
            using (FloatingWindow overlay = new()
            {
                FormBorderStyle = FormBorderStyle.None, BackColor = Color.Fuchsia, TopMost = true,
                ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                Location = page.PointToScreen(new Point(page.Width / 3, page.Height / 3)), ClientSize = new Size(120, 80)
            })
            {
                overlay.Show(); await Task.Delay(80);
                ((RadioButton)page.Controls.Find("themeDark", true).Single()).Checked = true;
                PageTransition covered = Field<PageTransition>(main, "pageTransition");
                Field<UiTween>(covered, "progress").SnapTo(0);
                using Bitmap own = Render(covered);
                Point center = page.PointToClient(overlay.PointToScreen(new Point(overlay.Width / 2, overlay.Height / 2)));
                Color pixel = own.GetPixel(center.X, center.Y);
                Check(!(pixel.R > 240 && pixel.G < 15 && pixel.B > 240), "floating windows are excluded from the theme animation image");
                overlay.Hide(); Call(main, "EndPageTransition");
            }
            UiMotion.UserEnabled = false; working.AnimationsEnabled = false;
            ((CheckBox)page.Controls.Find("animationsToggle", true).Single()).Checked = false;
            ((RadioButton)page.Controls.Find("themeLight", true).Single()).Checked = true;
            Check(Field<PageTransition?>(main, "pageTransition") == null, "reduced motion applies the theme immediately without a crossfade cover");
        }
        finally
        {
            Call(main, "CloseSettings"); typeof(MainForm).GetField("preferences", Private)!.SetValue(main, original);
            Call(main, "ApplyAppearance"); main.TopMost = topmost; Cursor.Position = cursor;
        }
    }
}
