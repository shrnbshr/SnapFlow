using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class SettingsEndpointsRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Call(MainForm form, string name) => typeof(MainForm).GetMethod(name, Private)!.Invoke(form, null);
    private static T Find<T>(Control parent, string name) where T : Control => (T)parent.Controls.Find(name, true).Single();

    [DllImport("user32.dll")]
    private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    internal static async Task RunAsync(MainForm main, bool desktopPointer)
    {
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        Rectangle oldBounds = main.Bounds;
        Point cursor = Cursor.Position;
        bool oldTopMost = main.TopMost;
        try
        {
            main.TopMost = true;
            main.ForceActivate();
            int Scale(int x) => x * main.DeviceDpi / 96;
            Rectangle area = Screen.FromControl(main).WorkingArea;
            main.ClientSize = new Size(Math.Min(Scale(960), area.Width - Scale(32)), Math.Min(Scale(720), area.Height - Scale(64)));
            main.Location = new Point(area.Left + (area.Width - main.Width) / 2, area.Top + (area.Height - main.Height) / 2);
            Call(main, "ReleaseCaptureHotkeys");
            ApplicationPreferences working = original.Clone();
            working.AnimationsEnabled = working.TransparencyEnabled = true;
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, working);
            Call(main, "ApplyAppearance");
            Call(main, "OpenSettings");
            await Task.Delay(400);

            SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
            Panel viewport = Field<Panel>(page, "scroller");
            Control heading = Find<Control>(page, "heading"), footer = Find<Control>(page, "footer");
            Button save = Find<Button>(page, "saveSettingsButton"), reset = Find<Button>(page, "resetSettingsButton");
            Button cancel = Find<Button>(page, "cancelSettingsButton");
            Rectangle Bounds(Control control) => control.RectangleToScreen(control.ClientRectangle);
            viewport.AutoScrollPosition = Point.Empty;
            Rectangle visible = Bounds(viewport);
            Check(visible.Contains(Bounds(heading)) && !visible.IntersectsWith(Bounds(save)),
                "the page starts with the title visible and save below the viewport");
            Check(heading.Parent == footer.Parent && heading.Parent?.Parent == viewport && page.Controls.Count == 1,
                "both sections live inside the same scroll canvas");

            foreach (AppearanceMode mode in new[] { AppearanceMode.Dark, AppearanceMode.Light })
            foreach (bool transparent in new[] { true, false })
            {
                string scenario = $"{main.DeviceDpi} DPI/{mode}/transparency={transparent}";
                Find<RadioButton>(page, "theme" + mode).Checked = true;
                Find<MotionToggle>(page, "transparencyToggle").Checked = transparent;
                viewport.AutoScrollPosition = new Point(0, viewport.DisplayRectangle.Height);
                viewport.Invalidate(true);
                viewport.Update();
                await Task.Delay(100);
                visible = Bounds(viewport);
                Check(!visible.IntersectsWith(Bounds(heading)) && visible.Contains(Bounds(save)) &&
                    visible.Contains(Bounds(cancel)) && visible.Contains(Bounds(reset)),
                    scenario + ": bottom shows all three actions and the title has scrolled away");
                foreach (string name in new[] { "aboutUpstream", "aboutLicense", "aboutNotice" })
                {
                    AboutDocumentButton button = Find<AboutDocumentButton>(page, name);
                    Check(visible.Contains(Bounds(button)), scenario + "/" + name + ": bottom link and its complete shadow fit in the viewport");
                    await CheckHover(button, main.PointToScreen(new Point(Scale(8), Scale(8))), scenario + "/" + name, desktopPointer);
                }
            }
        }
        finally
        {
            Cursor.Position = cursor;
            Call(main, "CloseSettings");
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, original);
            main.Bounds = oldBounds;
            main.TopMost = oldTopMost;
            Call(main, "ApplyAppearance");
            Call(main, "RegisterCaptureHotkeys");
        }
    }

    private static async Task CheckHover(AboutDocumentButton button, Point rest, string scenario, bool desktopPointer)
    {
        if (desktopPointer)
        {
            Cursor.Position = rest;
            await Task.Delay(230);
        }
        float scale = button.DeviceDpi / 96f;
        RectangleF face = ButtonElevation.FaceBounds(button.ClientSize, scale);
        Point pointer = button.PointToScreen(new Point(button.Width / 2, button.Height / 2));
        // Sample under the face where the animated shadow falls on the card background.
        Point localShadow = new(button.Width / 2, (int)Math.Ceiling(face.Bottom + scale));
        Point shadow = button.PointToScreen(localShadow);
        if (desktopPointer && !LogicalToPhysicalPointForPerMonitorDPI(button.Handle, ref shadow))
            throw new InvalidOperationException("Cannot map the shadow to desktop pixels.");
        Screenshot capture = new() { RemoveOutsideScreenArea = false };
        using Bitmap rendered = new(button.Width, button.Height);
        int SampleShadow()
        {
            if (!desktopPointer)
            {
                button.DrawToBitmap(rendered, button.ClientRectangle);
                return rendered.GetPixel(localShadow.X, localShadow.Y).R;
            }
            using Bitmap image = capture.CaptureRectangle(new Rectangle(shadow, new Size(1, 1)));
            return image.GetPixel(0, 0).R;
        }
        int enters = 0, leaves = 0, resting = SampleShadow();
        void Trace(string state)
        {
            if (desktopPointer)
                Console.WriteLine($"MOUSE {state}: cursor={Cursor.Position}, hit={WindowFromPoint(Cursor.Position):X}, button={button.Handle:X}");
        }
        EventHandler entered = (_, _) => { enters++; Trace("enter"); };
        EventHandler left = (_, _) => { leaves++; Trace("leave"); };
        button.MouseEnter += entered;
        button.MouseLeave += left;
        try
        {
            Stopwatch clock = Stopwatch.StartNew();
            if (desktopPointer) Cursor.Position = pointer;
            else typeof(AboutDocumentButton).GetMethod("OnMouseEnter", Private)!.Invoke(button, [EventArgs.Empty]);
            List<int> levels = [resting];
            double? firstChange = null;
            while (clock.ElapsedMilliseconds < 280)
            {
                int level = SampleShadow();
                if (level < resting) firstChange ??= clock.Elapsed.TotalMilliseconds;
                levels.Add(level);
                await Task.Delay(5);
            }
            if (desktopPointer) Console.WriteLine($"POINTER {scenario}: requested={pointer}, cursor={Cursor.Position}, enters={enters}, leaves={leaves}");
            Check(enters == 1 && leaves == 0, scenario + ": a single mouse enter starts the hover");
            Check(firstChange.HasValue && levels[^1] < resting &&
                (!UiMotion.Enabled || levels.Distinct().Count() > 2) &&
                levels.Zip(levels.Skip(1)).All(pair => pair.Second <= pair.First + 1),
                scenario + $": shadow darkens progressively (first sampled change {firstChange:F1} ms)");
            if (desktopPointer) Cursor.Position = rest;
            else typeof(AboutDocumentButton).GetMethod("OnMouseLeave", Private)!.Invoke(button, [EventArgs.Empty]);
            await Task.Delay(230);
            Check(leaves == 1 && SampleShadow() == resting, scenario + ": the shadow returns to its resting pixels");
        }
        finally
        {
            button.MouseEnter -= entered;
            button.MouseLeave -= left;
        }
    }
}
