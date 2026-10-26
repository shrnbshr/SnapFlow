using ShareX.HelpersLib;
using ShareX.Roll;
using System.Diagnostics;
using System.IO;
using System.Reflection;

internal static class SettingsMotionPerformanceRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static void Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private)!.Invoke(owner, args);
    private static T Find<T>(Control parent, string name) where T : Control => (T)parent.Controls.Find(name, true).Single();
    private sealed class Probe : NativeWindow, IDisposable
    {
        private readonly Control control;
        private readonly Dictionary<string, (long Ticks, int Count)> times;
        internal Probe(Control control, Dictionary<string, (long, int)> times)
        {
            this.control = control; this.times = times;
            control.HandleCreated += Created; control.HandleDestroyed += Destroyed;
            if (control.IsHandleCreated) Created(null, EventArgs.Empty);
        }
        private void Created(object? sender, EventArgs e) => AssignHandle(control.Handle);
        private void Destroyed(object? sender, EventArgs e) => ReleaseHandle();
        protected override void WndProc(ref Message message)
        {
            if (message.Msg != 0xF) { base.WndProc(ref message); return; }
            long started = Stopwatch.GetTimestamp();
            base.WndProc(ref message);
            string key = control.GetType().Name + ":" + control.Name + ":" + control.ClientSize;
            var previous = times.GetValueOrDefault(key);
            times[key] = (previous.Ticks + Stopwatch.GetTimestamp() - started, previous.Count + 1);
        }
        public void Dispose()
        {
            control.HandleCreated -= Created; control.HandleDestroyed -= Destroyed; ReleaseHandle();
        }
    }
    private static IEnumerable<Control> Descendants(Control parent)
    {
        yield return parent;
        foreach (Control child in parent.Controls)
            foreach (Control control in Descendants(child)) yield return control;
    }
    private static void Cadence(string name, List<double> frames)
    {
        double[] intervals = frames.Zip(frames.Skip(1), (a, b) => b - a).Order().ToArray();
        if (intervals.Length == 0) throw new InvalidOperationException(name + ": animation has no intermediate frames");
        Console.WriteLine($"CADENCE {name}: frames={frames.Count}, medianMs={intervals[intervals.Length / 2]:F2}, p95Ms={intervals[(int)((intervals.Length - 1) * .95)]:F2}, maxMs={intervals[^1]:F2}");
    }
    internal static async Task RunAsync(MainForm main, bool baseline)
    {
        string output = Path.GetFullPath($"artifacts/settings-animation/{(baseline ? "before" : "after")}-{main.DeviceDpi}");
        Directory.CreateDirectory(output);
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        bool topmost = main.TopMost;
        bool motion = UiMotion.UserEnabled;
        Size size = main.Size;
        Point cursor = Cursor.Position;
        try
        {
            Call(main, "ReleaseCaptureHotkeys");
            ApplicationPreferences working = original.Clone();
            working.TransparencyEnabled = working.AnimationsEnabled = true;
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, working);
            Call(main, "ApplyAppearance");
            main.TopMost = true; main.ForceActivate(); Cursor.Position = Point.Empty;
            Call(main, "OpenSettings");
            await Task.Delay(400);
            SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
            Panel viewport = Field<Panel>(page, "scroller");
            SettingsScrollHost host = Find<SettingsScrollHost>(page, "settingsScrollHost");
            Control bar = Find<Control>(page, "settingsScrollBar");
            SettingsReveal reveal = Find<SettingsReveal>(page, "scrollingReveal");
            SettingsDisclosure disclosure = Find<SettingsDisclosure>(page, "scrollingOptionsButton");
            Control content = Field<Control>(page, "content"), editor = Field<Control>(reveal, "editor");
            foreach (AppearanceMode theme in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            foreach (bool maximized in new[] { false, true })
            {
                main.WindowState = maximized ? FormWindowState.Maximized : FormWindowState.Normal;
                if (!maximized) main.ClientSize = new Size(960 * main.DeviceDpi / 96, 720 * main.DeviceDpi / 96);
                Find<RadioButton>(page, "theme" + theme).Checked = true;
                Find<Button>(page, "saveSettingsButton").Focus();
                viewport.AutoScrollPosition = Point.Empty;
                await Task.Delay(200);
                if (!UiMotion.Enabled) throw new InvalidOperationException("Real animation measurements require enabled Windows and app animations");
                Dictionary<string, (long Ticks, int Count)> times = new();
                Probe[] probes = Descendants(page).Select(c => new Probe(c, times)).ToArray();
                string context = theme + (maximized ? "-max" : "-normal");
                try
                {
                    int updates = host.ScrollUpdates;
                    long ticks = host.ScrollUpdateTicks;
                    List<double> frames = new(); Stopwatch clock = Stopwatch.StartNew();
                    int previousOffset = -1;
                    PaintEventHandler painted = (_, _) =>
                    {
                        int offset = -viewport.AutoScrollPosition.Y;
                        if (offset != previousOffset) { frames.Add(clock.Elapsed.TotalMilliseconds); previousOffset = offset; }
                    };
                    bar.Paint += painted;
                    for (int i = 0; i < 6; i++)
                    {
                        Call(viewport, "OnMouseWheel", new HandledMouseEventArgs(MouseButtons.None, 0, 8, 8, -120));
                        await Task.Delay(12);
                    }
                    await Task.Delay(450);
                    bar.Paint -= painted;
                    int count = host.ScrollUpdates - updates;
                    Console.WriteLine($"COST {context}-scroll: updates={count}, meanSubmitMs={(host.ScrollUpdateTicks - ticks) * 1000d / Stopwatch.Frequency / Math.Max(1, count):F3}");
                    Cadence(context + "-scroll", frames);
                    List<string> rows = ["phase,milliseconds,height"];
                    foreach (bool expand in new[] { true, false })
                    {
                        viewport.ScrollControlIntoView(disclosure);
                        await Task.Delay(80);
                        int layouts = 0, editorLayouts = 0;
                        LayoutEventHandler laidOut = (_, _) => layouts++, editorLaidOut = (_, _) => editorLayouts++;
                        content.Layout += laidOut; editor.Layout += editorLaidOut;
                        frames.Clear(); clock.Restart();
                        EventHandler changed = (_, _) =>
                        {
                            frames.Add(clock.Elapsed.TotalMilliseconds);
                            rows.Add(FormattableString.Invariant($"{(expand ? "expand" : "collapse")},{clock.Elapsed.TotalMilliseconds:F3},{reveal.Height}"));
                        };
                        reveal.SizeChanged += changed;
                        ticks = reveal.UpdateTicks; updates = reveal.UpdatedFrames;
                        Control group = reveal.Parent!;
                        int originalHeight = group.Height;
                        var followingRows = content.Controls.Cast<Control>()
                            .Where(c => c.Visible && c != group && c.Top >= group.Bottom)
                            .Select(c => (Control: c, Top: c.Top)).ToArray();
                        disclosure.PerformClick();
                        for (int sample = 0; sample < 13; sample++)
                        {
                            await Task.Delay(50);
                            if (group.Height != disclosure.Height + reveal.Height || reveal.Bottom != group.ClientSize.Height ||
                                followingRows.Any(row => row.Control.Top - row.Top != group.Height - originalHeight))
                                throw new InvalidOperationException("Animated reveal must keep native row bounds, following rows and hit targets aligned.");
                        }
                        reveal.SizeChanged -= changed; content.Layout -= laidOut; editor.Layout -= editorLaidOut;
                        count = reveal.UpdatedFrames - updates;
                        Console.WriteLine($"COST {context}-{(expand ? "expand" : "collapse")}: updates={count}, meanUpdateMs={(reveal.UpdateTicks - ticks) * 1000d / Stopwatch.Frequency / Math.Max(1, count):F3}, contentLayouts={layouts}, editorLayouts={editorLayouts}, settled={!reveal.IsAnimating}");
                        Console.WriteLine($"STAGES {context}: totalLayoutMs={reveal.LayoutTicks * 1000d / Stopwatch.Frequency:F2}, totalPresentMs={reveal.PresentTicks * 1000d / Stopwatch.Frequency:F2}");
                        Cadence(context + (expand ? "-expand" : "-collapse"), frames);
                        if (reveal.IsAnimating || (expand ? reveal.Height <= 0 : reveal.Height != 0)) throw new InvalidOperationException("Reveal must settle at the exact target");
                        if (group.Height != disclosure.Height + reveal.Height || reveal.Bottom != group.ClientSize.Height)
                            throw new InvalidOperationException("Reveal must update its native parent height without clipping or leaving a gap.");
                    }
                    File.WriteAllLines(Path.Combine(output, context + ".csv"), rows);
                    foreach (var entry in times.OrderByDescending(p => p.Value.Ticks).Take(8))
                        Console.WriteLine($"PAINT {context} {entry.Key}: count={entry.Value.Count}, totalMs={entry.Value.Ticks * 1000d / Stopwatch.Frequency:F2}, meanMs={entry.Value.Ticks * 1000d / Stopwatch.Frequency / entry.Value.Count:F3}");
                    foreach (string name in baseline ? Array.Empty<string>() : new[] { "headingBackdrop", "footerBackdrop" })
                    {
                        SettingsBarBackdrop backdrop = Field<SettingsBarBackdrop>(page, name);
                        int builds = Field<int>(backdrop, "Builds");
                        double divisor = Stopwatch.Frequency / 1000d * Math.Max(1, builds);
                        Console.WriteLine($"MATERIAL {context}-{name}: builds={builds}, reuses={Field<int>(backdrop, "Reuses")}, captureMs={Field<long>(backdrop, "CaptureTicks") / divisor:F3}, filterMs={Field<long>(backdrop, "FilterTicks") / divisor:F3}, upscaleMs={Field<long>(backdrop, "UpscaleTicks") / divisor:F3}, alphaMs={Field<long>(backdrop, "AlphaTicks") / divisor:F3}");
                    }
                }
                finally { foreach (Probe probe in probes) probe.Dispose(); }
            }
            disclosure.PerformClick(); await Task.Delay(60);
            int current = reveal.Height;
            disclosure.PerformClick();
            if (reveal.Height != current) throw new InvalidOperationException("Reversing collapse must retain the visible height");
            await Task.Delay(60); current = reveal.Height;
            disclosure.PerformClick();
            if (reveal.Height != current) throw new InvalidOperationException("Reversing expansion must retain the visible height");
            await Task.Delay(600);
            if (reveal.IsAnimating || reveal.Height != Field<int>(reveal, "fullHeight")) throw new InvalidOperationException("Reversed expansion must finish at full height");
            Console.WriteLine("PASS: repeated reversal continues from the current height and reaches the exact destination");
            UiMotion.UserEnabled = false;
            disclosure.PerformClick();
            if (reveal.IsAnimating || reveal.Height != 0 || editor.Visible) throw new InvalidOperationException("Reduced motion must collapse immediately");
            disclosure.PerformClick();
            if (reveal.IsAnimating || reveal.Height != Field<int>(reveal, "fullHeight") || !editor.Visible) throw new InvalidOperationException("Reduced motion must expand immediately");
            Console.WriteLine("PASS: disabled animations finish immediately and preserve native editor visibility");
            UiMotion.UserEnabled = true;
            disclosure.PerformClick(); await Task.Delay(30);
            Call(main, "CloseSettings");
            await Task.Delay(350);
            if (reveal.IsAnimating || page.Visible) throw new InvalidOperationException("Leaving settings must release the active frame clock and hide the cached page");
            Console.WriteLine("PASS: leaving settings releases its active animation clock");
        }
        finally
        {
            main.WindowState = FormWindowState.Normal; main.Size = size; main.TopMost = topmost; Cursor.Position = cursor;
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, original);
            UiMotion.UserEnabled = motion;
        }
    }
}
