using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.Reflection;
using System.IO;

internal static class UiRegression
{
    private static readonly string Output = Path.GetFullPath("artifacts/hig-refresh");
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, arguments);
    private static T Find<T>(Control target, string name) where T : Control => (T)target.Controls.Find(name, true).Single();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private static void Render(MainForm main, string name)
    {
        using Bitmap bitmap = new(main.Width, main.Height);
        main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, main.Size));
        bitmap.Save(Path.Combine(Output, name + "-" + main.DeviceDpi + ".png"), ImageFormat.Png);
    }

    internal static async Task RunAsync(MainForm main)
    {
        Directory.CreateDirectory(Output);
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences").Clone();
        int windows = Application.OpenForms.Count;
        Size normalSize = main.Size;
        try
        {
            Button settings = Find<Button>(main, "settingsButton");
            Console.WriteLine($"METRIC: startup settings bounds={settings.Bounds}, dpi={main.DeviceDpi}");
            Check(settings.Width <= 160 * main.DeviceDpi / 96,
                "startup settings button stays compact at the current display scale");
            Check(settings.Width >= TextRenderer.MeasureText(settings.Text, settings.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 32 * main.DeviceDpi / 96,
                "startup settings button has room for its icon and label");
            main.TopMost = true;
            main.ForceActivate();
            foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                ApplicationPreferences draft = original.Clone();
                draft.Appearance = mode;
                Set(main, "preferences", draft);
                Call(main, "ApplyAppearance");
                await Task.Delay(280);
                TableLayoutPanel surface = Field<TableLayoutPanel>(main, "previewFrame");
                foreach (CaptureShortcut action in ApplicationPreferences.Actions)
                {
                    Button button = Find<Button>(main, action.Kind + "Button");
                    Check(button.Visible && button.Enabled && button.RectangleToScreen(button.ClientRectangle).Right < surface.PointToScreen(Point.Empty).X,
                        action.Title + " is usable in the left action column");
                }
                Check(surface.Width > 360 * main.DeviceDpi / 96, "preview receives the main content area");
                Render(main, "main-" + mode);
                if (main.DeviceDpi > 96)
                {
                    using Bitmap desktop = new Screenshot().CaptureWindow(main.Handle);
                    desktop.Save(Path.Combine(Output, "main-live-" + mode + ".png"), ImageFormat.Png);
                }
                Find<Button>(main, "settingsButton").PerformClick();
                await Task.Delay(280);
                Control motionRow = Find<Control>(main, "motionRow");
                CheckBox toggle = Find<CheckBox>(main, "animationsToggle");
                Check(motionRow.RectangleToScreen(motionRow.ClientRectangle).Contains(toggle.RectangleToScreen(toggle.ClientRectangle)),
                    "motion switch stays fully inside its rounded settings row");
                Render(main, "settings-final-" + mode);
                Find<Button>(main, "cancelSettingsButton").PerformClick();
                await Task.Delay(280);
            }

            ApplicationPreferences animated = original.Clone();
            animated.AnimationsEnabled = true;
            Set(main, "preferences", animated);
            Call(main, "ApplyAppearance");
            CaptureModeButton modeButton = Find<CaptureModeButton>(main, "regionButton");
            UiTween hover = Field<UiTween>(modeButton, "hover");
            hover.SnapTo(0);
            bool animationsAllowed = UiMotion.Enabled;
            Console.WriteLine("Windows permits animation: " + animationsAllowed);
            Call(modeButton, "OnMouseEnter", EventArgs.Empty);
            if (animationsAllowed)
            {
                Check(hover.IsRunning, "hover feedback starts a bounded animation");
                await Task.Delay(45);
                Check(hover.Value > 0 && hover.Value < 1, "hover animation has intermediate frames");
                await Task.Delay(180);
            }
            Check(!hover.IsRunning && hover.Value == 1, "hover timer stops after completing");
            Call(modeButton, "OnMouseLeave", EventArgs.Empty);
            await Task.Delay(180);

            Find<Button>(main, "settingsButton").PerformClick();
            if (animationsAllowed)
            {
                Check(Field<PageTransition?>(main, "pageTransition") is { IsAnimating: true }, "in-window navigation starts a visual transition");
                if (main.DeviceDpi > 96)
                {
                    List<Bitmap> frames = new();
                    try
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            await Task.Delay(25);
                            frames.Add(new Screenshot().CaptureWindow(main.Handle));
                        }
                        Check(!ImageHelpers.CompareImages(frames[0], frames[^1]), "real desktop frames show the navigation transition changing visually");
                        for (int i = 0; i < frames.Count; i++)
                            frames[i].Save(Path.Combine(Output, "transition-live-" + i.ToString("D2") + ".png"), ImageFormat.Png);
                    }
                    finally { foreach (Bitmap frame in frames) frame.Dispose(); }
                }
            }
            await Task.Delay(250);
            Check(Field<PageTransition?>(main, "pageTransition") == null, "navigation releases snapshots and stops its timer");
            Check(Application.OpenForms.Count == windows, "animated navigation creates no popup windows");
            Find<CheckBox>(main, "animationsToggle").Checked = false;
            Check(!UiMotion.UserEnabled && !UiMotion.Enabled, "animation toggle previews reduced motion immediately");
            Find<Button>(main, "cancelSettingsButton").PerformClick();
            Check(UiMotion.UserEnabled, "cancel restores the original enabled animation preference");
            Check(Field<PageTransition?>(main, "pageTransition") == null, "return while motion is off is immediate");

            ApplicationPreferences disabled = animated.Clone();
            disabled.AnimationsEnabled = false;
            Set(main, "preferences", disabled);
            Call(main, "ApplyAppearance");
            hover.SnapTo(0);
            Call(modeButton, "OnMouseEnter", EventArgs.Empty);
            Check(hover.Value == 1 && !hover.IsRunning, "disabled animations apply interaction states without starting a timer");
            Find<Button>(main, "settingsButton").PerformClick();
            Check(Field<PageTransition?>(main, "pageTransition") == null, "disabled motion navigates without an overlay");
            Check(!Find<CheckBox>(main, "animationsToggle").Checked, "settings reflects the disabled animation preference");
            Find<Button>(main, "cancelSettingsButton").PerformClick();

            string path = Path.Combine(Output, "motion-preferences.json");
            disabled.Save(path);
            Check(!ApplicationPreferences.Load(path).AnimationsEnabled, "reduced-motion preference survives save and reload");
            File.WriteAllText(path, "{\"Appearance\":0,\"Shortcuts\":{}}");
            Check(ApplicationPreferences.Load(path).AnimationsEnabled, "older preference files retain animation defaults");

            Set(main, "preferences", animated);
            Call(main, "ApplyAppearance");
            for (int i = 0; i < 4; i++)
            {
                Call(main, "OpenSettings");
                Call(main, "CloseSettings");
            }
            await Task.Delay(300);
            Check(Field<PageTransition?>(main, "pageTransition") == null && Application.OpenForms.Count == windows,
                "rapid navigation cancels previous transitions and leaves no overlay");
            if (main.DeviceDpi > 96)
            {
                Call(main, "OpenSettings");
                Button back = Find<Button>(main, "cancelSettingsButton");
                Point point = back.PointToScreen(new Point(back.Width / 2, back.Height / 2));
                Check(InputHelpers.SendMouseClick(point), "physical pointer click sent while navigation is animating");
                await Task.Delay(90);
                Check(Field<SettingsPage?>(main, "settingsPage") == null, "visual transition does not block the return button");
                await Task.Delay(250);
            }
            Call(main, "OpenSettings");
            main.Hide();
            Check(Field<PageTransition?>(main, "pageTransition") == null, "hiding the capture window cancels an active visual transition");
            main.Show();
            Call(main, "CloseSettings");
            await Task.Delay(250);

            main.Size = main.MinimumSize;
            await Task.Delay(180);
            foreach (CaptureShortcut action in ApplicationPreferences.Actions)
            {
                Button button = Find<Button>(main, action.Kind + "Button");
                Check(main.RectangleToScreen(main.ClientRectangle).Contains(button.RectangleToScreen(button.ClientRectangle)),
                    action.Title + " remains reachable at minimum size");
            }
            Check(main.Controls.Find("scrollingOptionsButton", true).Length == 0,
                "capture workspace keeps scrolling options inside settings");
            Render(main, "main-minimum");
            Find<Button>(main, "settingsButton").PerformClick();
            Button scrollingOptions = Find<Button>(main, "scrollingOptionsButton");
            for (Control? parent = scrollingOptions.Parent; parent != null; parent = parent.Parent)
                if (parent is Panel { AutoScroll: true } panel) panel.ScrollControlIntoView(scrollingOptions.Parent!);
            Check(scrollingOptions.Visible && main.RectangleToScreen(main.ClientRectangle).Contains(scrollingOptions.RectangleToScreen(scrollingOptions.ClientRectangle)),
                "scrolling options remain reachable inside settings at minimum size");
            Render(main, "settings-scrolling-minimum");
        }
        finally
        {
            Call(main, "CloseSettings");
            Set(main, "preferences", original);
            Call(main, "ApplyAppearance");
            main.Size = normalSize;
            main.TopMost = false;
        }
    }
}
