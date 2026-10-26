using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class WindowFrameRegression
{
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private sealed class FrameMessages : NativeWindow, IDisposable
    {
        internal int IconChanges, FrameChanges;
        internal readonly List<string> Events = new();
        internal FrameMessages(IntPtr handle) => AssignHandle(handle);
        internal void Clear() { IconChanges = 0; FrameChanges = 0; Events.Clear(); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x80) IconChanges++; // WM_SETICON
            if (m.Msg is 0x6 or 0x1C or 0x7D or 0x86) FrameChanges++; // activation / style / nonclient activation
            if (m.Msg is 0x6 or 0x7 or 0x8 or 0x1C or 0x46 or 0x47 or 0x80 or 0x85 or 0x86 or 0x18)
                Events.Add($"{Environment.TickCount64} WM_{m.Msg:X}: w={m.WParam:X}, l={m.LParam:X}, other={Control.FromHandle(m.LParam)?.Name}");
            base.WndProc(ref m);
        }
        public void Dispose() => ReleaseHandle();
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/window-frame-fix/" + (Environment.GetEnvironmentVariable("SNAPFLOW_TEST_STAGE") ?? "fixed"));
        Directory.CreateDirectory(output);
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        string originalPath = Field<string>(main, "preferencesPath");
        List<string> failures = new();
        try
        {
            main.TopMost = false;
            main.ForceActivate();
            Set(main, "preferencesPath", Path.Combine(output, "test-preferences.json"));
            foreach (AppearanceMode mode in new[] { AppearanceMode.Dark, AppearanceMode.Light })
            {
                ApplicationPreferences preferences = original.Clone();
                preferences.Appearance = mode;
                foreach (string kind in preferences.Shortcuts.Keys.ToArray()) preferences.Shortcuts[kind] = Keys.None;
                Set(main, "preferences", preferences);
                Call(main, "ApplyAppearance");
                foreach (string buttonName in new[] { "cancelSettingsButton", "saveSettingsButton" })
                {
                    Find<Button>(main, "settingsButton").PerformClick();
                    AppearanceMode displayedMode = mode;
                    if (buttonName == "saveSettingsButton")
                    {
                        displayedMode = mode == AppearanceMode.Dark ? AppearanceMode.Light : AppearanceMode.Dark;
                        Find<RadioButton>(main, "theme" + displayedMode).Checked = true;
                    }
                    await Task.Delay(300);
                    main.ForceActivate();
                    await Task.Delay(120);
                    Check(NativeMethods.GetForegroundWindow() == main.Handle, "frame test owns the foreground before desktop sampling");
                    Rectangle bounds = Rectangle.Inflate(CaptureHelpers.GetWindowRectangle(main.Handle), 24, 24);
                    Rectangle client = main.RectangleToScreen(main.ClientRectangle);
                    InputHelpers.SendMouseMove(new Point(bounds.Left - 20, bounds.Top));
                    await Task.Delay(100);
                    using FrameMessages messages = new(main.Handle);
                    List<Bitmap> frames = new();
                    using CancellationTokenSource stop = new();
                    using ManualResetEventSlim ready = new();
                    Task sampler = Task.Run(() =>
                    {
                        try
                        {
                            while (!stop.IsCancellationRequested)
                            {
                                frames.Add(new Screenshot { RemoveOutsideScreenArea = false }.CaptureRectangle(bounds));
                                ready.Set();
                                Thread.Sleep(8);
                            }
                        }
                        finally { ready.Set(); }
                    });
                    try
                    {
                        await Task.Run(() => ready.Wait());
                        await Task.Delay(40);
                        messages.Clear();
                        Button button = Find<Button>(main, buttonName);
                        Point click = button.PointToScreen(new Point(button.Width / 2, button.Height / 2));
                        Check(WindowFromPoint(click) == button.Handle,
                            "physical click target is the intended SnapFlow button");
                        Check(InputHelpers.SendMouseClick(click), "physical click delivered: " + buttonName);
                        await Task.Delay(400);
                    }
                    finally { stop.Cancel(); await sampler; }
                    try
                    {
                        string prefix = mode + "-" + buttonName;
                        File.WriteAllLines(Path.Combine(output, prefix + "-messages.log"), messages.Events);
                        frames[0].Save(Path.Combine(output, prefix + "-before.png"), ImageFormat.Png);
                        frames[^1].Save(Path.Combine(output, prefix + "-after.png"), ImageFormat.Png);
                        double largestChange = 0;
                        for (int i = 0; i < frames.Count; i++)
                        {
                            frames[i].Save(Path.Combine(output, prefix + "-frame-" + i.ToString("D2") + ".png"), ImageFormat.Png);
                            int changed = 0, sampled = 0;
                            for (int y = 2; y < bounds.Height - 2; y += 2)
                                for (int x = 2; x < bounds.Width - 2; x += 2)
                                {
                                    if (client.Contains(bounds.X + x, bounds.Y + y)) continue;
                                    sampled++;
                                    Color a = frames[0].GetPixel(x, y), b = frames[i].GetPixel(x, y);
                                    if (Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))) > 20) changed++;
                                }
                            largestChange = Math.Max(largestChange, (double)changed / sampled);
                        }
                        Console.WriteLine($"{prefix}: WM_SETICON={messages.IconChanges}, frame/activation messages={messages.FrameChanges}, max border/caption/shadow difference={largestChange:P3}, samples={frames.Count}");
                        if (messages.IconChanges != 0 || messages.FrameChanges != 0 || largestChange > .005) failures.Add(prefix + " unnecessarily refreshes the native frame");
                        Check(Field<SettingsPage?>(main, "settingsPage") == null, prefix + " returns to the workspace");
                        Check(NativeMethods.DwmGetWindowAttribute(main.Handle, 20, out int dark, sizeof(int)) == 0 && (dark != 0) == (displayedMode == AppearanceMode.Dark), prefix + " retains the native frame theme");
                        if (buttonName == "saveSettingsButton")
                            Check(ApplicationPreferences.Load(Path.Combine(output, "test-preferences.json")).Appearance == displayedMode, "saved theme survives the return to the workspace");
                    }
                    finally { foreach (Bitmap frame in frames) frame.Dispose(); }
                }
                Find<Button>(main, "settingsButton").PerformClick();
                AppearanceMode savedMode = Field<ApplicationPreferences>(main, "preferences").Appearance;
                Find<RadioButton>(main, "theme" + (savedMode == AppearanceMode.Dark ? AppearanceMode.Light : AppearanceMode.Dark)).Checked = true;
                Find<Button>(main, "cancelSettingsButton").PerformClick();
                Check(NativeMethods.DwmGetWindowAttribute(main.Handle, 20, out int restored, sizeof(int)) == 0 && (restored != 0) == (savedMode == AppearanceMode.Dark), "cancel restores the saved native frame theme after preview");
            }
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("; ", failures));
            Console.WriteLine("PASS: return and save keep the native border and caption stable in both themes");
        }
        finally
        {
            Call(main, "CloseSettings");
            Set(main, "preferences", original);
            Set(main, "preferencesPath", originalPath);
            Call(main, "ApplyAppearance");
            Call(main, "RegisterCaptureHotkeys");
            main.TopMost = false;
        }
    }
}
