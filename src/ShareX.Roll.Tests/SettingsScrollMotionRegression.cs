using ShareX.Roll;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;

internal static class SettingsScrollMotionRegression
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void Input(Control control, string method, object args) => control.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, [args]);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private static void Wheel(Control control, int delta) => Input(control, "OnMouseWheel", new HandledMouseEventArgs(MouseButtons.None, 0, 8, 8, delta));
    private static void NativeWheel(Control control, int delta)
    {
        Point point = control.PointToScreen(new Point(control.Width / 2, control.Height / 2));
        SendMessage(control.Handle, 0x20A, (IntPtr)(delta << 16), (IntPtr)((point.Y << 16) | (point.X & 0xFFFF)));
    }

    internal static async Task RunAsync(MainForm main, bool baseline, bool record)
    {
        string output = Path.GetFullPath("artifacts/settings-scroll-motion");
        Directory.CreateDirectory(output);
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        Size originalSize = main.Size;
        bool originalMotion = UiMotion.UserEnabled;
        bool originalTopMost = main.TopMost;
        List<string> rows = ["theme,milliseconds,offset"];
        try
        {
            ApplicationPreferences draft = original.Clone();
            draft.AnimationsEnabled = true;
            Set(main, "preferences", draft);
            main.Size = main.MinimumSize;
            Find<Button>(main, "settingsButton").PerformClick();
            await Task.Delay(300);
            SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
            Panel viewport = Field<Panel>(page, "scroller");
            Control content = Field<TableLayoutPanel>(page, "content");
            Control bar = Find<Control>(page, "settingsScrollBar");
            SettingsScrollHost host = Find<SettingsScrollHost>(page, "settingsScrollHost");
            int Offset() => -viewport.AutoScrollPosition.Y;
            int Maximum() => Math.Max(0, viewport.DisplayRectangle.Height - viewport.ClientSize.Height);
            Console.WriteLine($"CONFIG: DPI={main.DeviceDpi}, animation={UiMotion.Enabled}, lines={SystemInformation.MouseWheelScrollLines}, smallChange={viewport.VerticalScroll.SmallChange}, maximum={Maximum()}");
            foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                Find<RadioButton>(page, "theme" + mode).Checked = true;
                Find<Button>(page, "saveSettingsButton").Focus();
                await Task.Delay(80);
                viewport.AutoScrollPosition = Point.Empty;
                List<(double Time, int Position)> trace = new();
                int layouts = 0;
                Stopwatch clock = Stopwatch.StartNew();
                EventHandler moved = (_, _) => trace.Add((clock.Elapsed.TotalMilliseconds, Offset()));
                LayoutEventHandler laidOut = (_, _) => layouts++;
                content.LocationChanged += moved;
                content.Layout += laidOut;
                Wheel(viewport, -120);
                int immediate = Offset();
                await Task.Delay(420);
                content.LocationChanged -= moved;
                content.Layout -= laidOut;
                foreach (var sample in trace)
                    rows.Add(FormattableString.Invariant($"{mode},{sample.Time:F3},{sample.Position}"));
                Console.WriteLine($"TRACE: {mode}, immediate={immediate}, final={Offset()}, nativePositionUpdates={trace.Count}, contentLayouts={layouts}, lastUpdateMs={(trace.Count == 0 ? 0 : trace[^1].Time):F2}");
                if (!baseline)
                {
                    Check(Offset() > 0, mode + ": wheel moves the native viewport");
                    if (UiMotion.Enabled)
                    {
                        Check(immediate > 0 && immediate < Offset(), mode + ": wheel responds immediately and then moves through intermediate positions");
                        Check(trace.Count >= 3, mode + ": native content has multiple motion frames");
                        Check(trace.Zip(trace.Skip(1)).All(pair => pair.Second.Position >= pair.First.Position), mode + ": motion approaches the target without bounce");
                    }
                }
                if (record)
                {
                    // Separate visual sampling from the timing trace: capture itself costs time.
                    main.TopMost = true;
                    main.ForceActivate();
                    viewport.AutoScrollPosition = Point.Empty;
                    await Task.Delay(80);
                    Wheel(viewport, -480);
                    for (int frame = 0; frame < 6; frame++)
                    {
                        await Task.Delay(24);
                        using Bitmap image = new Screenshot().CaptureWindow(main.Handle);
                        image.Save(Path.Combine(output, $"live-{mode}-{main.DeviceDpi}-{frame}-y{Offset()}.png"), ImageFormat.Png);
                    }
                    await Task.Delay(220);
                }
            }
            if (!baseline)
            {
                int lines = SystemInformation.MouseWheelScrollLines;
                int step = lines < 0 ? viewport.ClientSize.Height : lines * Math.Max(viewport.Font.Height, viewport.VerticalScroll.SmallChange);
                if (lines == 0) throw new InvalidOperationException("Wheel tests require a nonzero Windows wheel-scroll setting");
                viewport.AutoScrollPosition = Point.Empty;
                Wheel(bar, -120);
                await Task.Delay(420);
                Check(Offset() == Math.Min(step, Maximum()), "wheel over the scrollbar uses the same distance as the viewport");

                viewport.AutoScrollPosition = Point.Empty;
                for (int i = 0; i < 4; i++) Wheel(viewport, -30);
                Check(Offset() == Math.Min(step, Maximum()) && !host.IsScrolling, "fine input moves directly and preserves fractional wheel deltas");
                viewport.AutoScrollPosition = Point.Empty;
                for (int i = 0; i < 120; i++) Wheel(viewport, -1);
                Check(Offset() == Math.Min(step, Maximum()), "subpixel wheel input accumulates without being lost");
                for (int i = 0; i < 120; i++) Wheel(viewport, 1);
                Check(Offset() == 0, "reversing fine input returns to the exact starting position");

                Wheel(viewport, -120);
                Wheel(viewport, -120);
                Wheel(viewport, -120);
                await Task.Delay(420);
                Check(Offset() == Math.Min(3 * step, Maximum()) && !host.IsScrolling, "rapid notches accumulate and settle instead of queuing separate animations");
                viewport.AutoScrollPosition = new Point(0, Maximum() / 2);
                Wheel(viewport, -120);
                await Task.Delay(35);
                int beforeReverse = Offset();
                Wheel(viewport, 120);
                Check(Offset() < beforeReverse, "reverse wheel input changes direction immediately");
                await Task.Delay(420);
                Check(Offset() < beforeReverse && !host.IsScrolling, "reversal abandons the previous downward destination");

                viewport.AutoScrollPosition = Point.Empty;
                Wheel(viewport, -120);
                Input(bar, "OnKeyDown", new KeyEventArgs(Keys.End));
                Check(Offset() == Maximum() && !host.IsScrolling, "End interrupts motion and reaches exact bottom immediately");
                Input(bar, "OnKeyDown", new KeyEventArgs(Keys.Home));
                await Task.Delay(220);
                Check(Offset() == 0 && !host.IsScrolling, "Home cancels pending movement without a later jump");

                Wheel(viewport, -120);
                bar.AccessibilityObject.Value = "100%";
                await Task.Delay(220);
                Check(Offset() == Maximum() && !host.IsScrolling, "accessibility position changes cancel animation");
                bar.AccessibilityObject.Value = "0%";
                Wheel(viewport, -120);
                viewport.ScrollControlIntoView(Find<Control>(page, "aboutNotice"));
                int focused = Offset();
                await Task.Delay(220);
                Check(focused > 0 && Offset() == focused && !host.IsScrolling, "focus navigation overrides pending wheel motion");

                viewport.AutoScrollPosition = Point.Empty;
                Wheel(viewport, -120);
                viewport.AutoScrollPosition = new Point(0, Maximum() / 2);
                int external = Offset();
                await Task.Delay(220);
                Check(Offset() == external && !host.IsScrolling, "external native scroll changes remain authoritative");

                viewport.AutoScrollPosition = Point.Empty;
                Wheel(viewport, -120);
                main.Width += 40;
                await Task.Delay(220);
                Check(!host.IsScrolling && Offset() <= Maximum(), "resize cancels motion against an outdated viewport");

                viewport.AutoScrollPosition = Point.Empty;
                UiMotion.UserEnabled = false;
                Wheel(viewport, -120);
                Check(Offset() == Math.Min(step, Maximum()) && !host.IsScrolling, "disabled animations scroll immediately with no timer");
                UiMotion.UserEnabled = true;
                viewport.AutoScrollPosition = Point.Empty;
                Wheel(viewport, -120);
                UiMotion.UserEnabled = false;
                await Task.Delay(80);
                Check(!host.IsScrolling && Offset() == Math.Min(step, Maximum()), "turning animations off settles active movement immediately");
                UiMotion.UserEnabled = true;

                viewport.AutoScrollPosition = Point.Empty;
                Find<Button>(page, "saveSettingsButton").Focus();
                Control languageCaption = Find<ComboBox>(page, "languageSelector").Parent!.Controls[0];
                NativeWheel(languageCaption.Controls[0], -120);
                await Task.Delay(420);
                Check(Offset() == Math.Min(step, Maximum()), "native wheel messages bubble from a child label exactly once");

                viewport.AutoScrollPosition = Point.Empty;
                Wheel(viewport, -120);
                Input(languageCaption.Controls[0], "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0));
                int clicked = Offset();
                await Task.Delay(220);
                Check(Offset() == clicked && !host.IsScrolling, "clicking visible settings interrupts motion even without a focus scroll");

                ComboBox language = Find<ComboBox>(page, "languageSelector");
                viewport.ScrollControlIntoView(language);
                language.Focus();
                int beforeChoice = Offset();
                NativeWheel(language, -120);
                await Task.Delay(100);
                Check(Offset() == beforeChoice && !host.IsScrolling, "focused language choice retains native wheel handling without moving the page");

                viewport.AutoScrollPosition = Point.Empty;
                int collapsedMaximum = Maximum();
                Wheel(viewport, -120);
                Find<Button>(page, "scrollingOptionsButton").PerformClick();
                await Task.Delay(320);
                Check(Maximum() > collapsedMaximum && Offset() >= 0 && Offset() <= Maximum() && !host.IsScrolling,
                    "expanding settings cancels motion against the old content range");
                NumericUpDown number = Find<NumericUpDown>(page, "nudStartDelay");
                number.Value = number.Minimum + 2 * number.Increment;
                viewport.ScrollControlIntoView(number);
                number.Focus();
                int beforeNumberScroll = Offset();
                decimal beforeNumber = number.Value;
                NativeWheel(number.Controls.OfType<TextBox>().Single(), -120);
                await Task.Delay(100);
                Check(number.Value < beforeNumber && Offset() == beforeNumberScroll, "focused numeric input keeps native wheel editing without scrolling the page");

                Console.WriteLine(FormattableString.Invariant($"COST: updates={host.ScrollUpdates}, meanNativeUpdateMs={host.ScrollUpdateTicks * 1000d / Stopwatch.Frequency / Math.Max(1, host.ScrollUpdates):F3}, maxNativeUpdateMs={host.MaximumScrollUpdateTicks * 1000d / Stopwatch.Frequency:F3}"));
                viewport.AutoScrollPosition = Point.Empty;
                Wheel(viewport, -120);
                Find<Button>(page, "backToCaptureButton").PerformClick();
                await Task.Delay(80);
                Check(page.IsDisposed && !host.IsScrolling, "leaving settings disposes the scrolling timer");
            }
        }
        finally
        {
            File.WriteAllLines(Path.Combine(output, $"{(baseline ? "before" : "after")}-{main.DeviceDpi}.csv"), rows);
            if (main.Controls.Find("backToCaptureButton", true).FirstOrDefault() is Button back) back.PerformClick();
            Set(main, "preferences", original);
            UiMotion.UserEnabled = originalMotion;
            main.TopMost = originalTopMost;
            main.Size = originalSize;
        }
    }
}
