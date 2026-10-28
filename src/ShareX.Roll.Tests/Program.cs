using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using System.IO;
using UIA = System.Windows.Automation;

internal static class Program
{
    private static int exitCode;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--save-location-child")) return SaveLocationRegression.ReadAfterRestart(args[^1]);
        if (args.Contains("--button-rendering"))
        {
            try { ButtonHoverRegression.VerifyRendering(); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        if (args.Contains("--stitch-regression"))
        {
            try { StitchingRegression.Run(); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
        Application.SetHighDpiMode(args.Contains("--dpi96") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--button-refinement"))
        {
            try { ButtonRefinementRegression.Run(); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        if (args.Contains("--button-elevation"))
        {
            try { ButtonElevationRegression.Run(args.Contains("--baseline"), args.Contains("--preview")); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        if (args.Contains("--shortcut-presentation"))
        {
            try { ShortcutPresentationRegression.Run(args.Contains("--baseline")); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        Stopwatch startup = Stopwatch.StartNew();
        using MainForm main = new();
        if (args.Contains("--startup-painting")) StartupPaintingRegression.ObserveStartup(main);
        if (args.Contains("--opening-presentation")) OpeningPresentationRegression.ObserveStartup(main);
        main.Shown += async (_, _) =>
        {
            SettingsOpenLatencyRegression.StartupMilliseconds = startup.Elapsed.TotalMilliseconds;
            try
            {
                if (args.Contains("--editor-initialization")) EditorOpeningFramesRegression.VerifyInitialization(args.Contains("--baseline"));
                else if (args.Contains("--editor-opening-frames")) await EditorOpeningFramesRegression.RunAsync(main, args.Contains("--baseline"), args.Contains("--pointer-opening"));
                else if (args.Contains("--opening-presentation")) await OpeningPresentationRegression.RunAsync(main, args.Contains("--baseline"));
                else if (args.Contains("--startup-painting")) await StartupPaintingRegression.RunAsync(main, args.Contains("--baseline"));
                else if (args.Contains("--button-hover")) await ButtonHoverRegression.RunAsync(main, args.Contains("--baseline"), args.Contains("--all-button-types"));
                else if (args.Contains("--transparency-startup")) TransparencySettingsRegression.VerifyStartup(main, !args.Contains("--expected-off"));
                else if (args.Contains("--theme-transition")) await ThemeTransitionRegression.RunAsync(main);
                else if (args.Contains("--transparency-flicker")) await TransparencyFlickerRegression.RunAsync(main, args.Contains("--baseline"));
                else if (args.Contains("--transparency-settings")) await TransparencySettingsRegression.RunAsync(main);
                else if (args.Contains("--backdrop-transition")) await BackdropTransitionRegression.RunAsync(main, args.Contains("--baseline"));
                else if (args.Contains("--acrylic-backdrop")) await AcrylicBackdropRegression.RunAsync(main);
                else if (args.Contains("--layout-consistency")) await LayoutConsistencyRegression.RunAsync(main);
                else if (args.Contains("--settings-bar-glass")) await SettingsBarGlassRegression.RunAsync(main);
                else if (args.Contains("--settings-viewport")) await SettingsViewportRegression.RunAsync(main, args.Contains("--baseline"));
                else if (args.Contains("--settings-restore")) await SettingsRestoreRegression.RunAsync(main, args.Contains("--baseline"));
                else if (args.Contains("--save-folder-presentation")) await SaveFolderPresentationRegression.RunAsync(main, args.Contains("--baseline"));
                else if (args.Contains("--save-location-visual")) await SaveLocationRegression.RunVisualAsync(main, args.Contains("--baseline"));
                else if (args.Contains("--save-location")) await SaveLocationRegression.RunAsync(main);
                else if (args.Contains("--settings-response-latency")) await SettingsOpenLatencyRegression.RunAsync(main, args.Contains("--baseline"), "artifacts/settings-response/entry");
                else if (args.Contains("--settings-reuse")) await SettingsPageReuseRegression.RunAsync(main);
                else if (args.Contains("--settings-open-latency")) await SettingsOpenLatencyRegression.RunAsync(main, args.Contains("--baseline"));
                else if (args.Contains("--settings-motion-performance")) await SettingsMotionPerformanceRegression.RunAsync(main, args.Contains("--baseline"));
                else if (args.Contains("--settings-scroll-cache")) await SettingsScrollCacheRegression.RunAsync(main);
                else if (args.Contains("--settings-scroll-presentation")) await SettingsScrollPresentationRegression.RunAsync(main, args.Contains("--baseline"), args.Contains("--maximized"));
                else if (args.Contains("--settings-scroll-motion")) await SettingsScrollMotionRegression.RunAsync(main, args.Contains("--baseline"), args.Contains("--record-scroll"));
                else if (args.Contains("--preview-zoom")) await PreviewZoomRegression.RunAsync(main);
                else if (args.Contains("--settings-live-painting")) await SettingsLivePaintingRegression.RunAsync(main);
                else if (args.Contains("--settings-menus")) await SettingsMenuRegression.RunAsync(main);
                else if (args.Contains("--settings-polish-layout")) await SettingsPolishRegression.RunAsync(main, enforceFrameBudget: false);
                else if (args.Contains("--settings-polish")) await SettingsPolishRegression.RunAsync(main);
                else if (args.Contains("--inline-scrolling")) await InlineScrollingRegression.RunAsync(main);
                else if (args.Contains("--settings-scrollbar")) await SettingsScrollbarRegression.RunAsync(main);
                else if (args.Contains("--annotation-physical")) await AnnotationRegression.RunPhysicalAsync(main);
                else if (args.Contains("--annotation")) await AnnotationRegression.RunAsync(main);
                else if (args.Contains("--crop-viewport")) await BoundaryCropRegression.RunViewportAsync(main);
                else if (args.Contains("--preview-actions")) await PreviewActionLayoutRegression.RunAsync(main);
                else if (args.Contains("--editor-options")) await EditorOptionsRegression.RunAsync(main);
                else if (args.Contains("--editor-effects")) await EditorEffectsRegression.RunAsync(main);
                else if (args.Contains("--editor-backdrop")) await EditorBackdropRegression.RunAsync(main);
                else if (args.Contains("--editor-chrome")) await EditorChromeRegression.RunAsync(main);
                else if (args.Contains("--edge-crop")) await BoundaryCropRegression.RunAsync(main, args.Contains("--simulate-input"));
                else if (args.Contains("--editor")) await EditorRegression.RunAsync(main);
                else if (args.Contains("--merged-capture")) await WorkspacePreviewRegression.RunMergedAsync(main);
                else if (args.Contains("--workspace-preview")) await WorkspacePreviewRegression.RunAsync(main);
                else if (args.Contains("--motion-continuity")) await MotionContinuityRegression.RunAsync(main);
                else if (args.Contains("--settings-motion-continuity")) await SettingsMotionContinuityRegression.RunAsync(main);
                else if (args.Contains("--preview-motion-continuity")) await PreviewMotionContinuityRegression.RunAsync(main);
                else if (args.Contains("--animation-cadence")) await AnimationCadenceRegression.RunAsync(main);
                else if (args.Contains("--animation-performance")) await AnimationPerformanceRegression.RunAsync(main);
                else if (args.Contains("--motion")) await MotionRegression.RunAsync(main);
                else if (args.Contains("--language")) await LanguageRegression.RunAsync(main);
                else if (args.Contains("--window-frame")) await WindowFrameRegression.RunAsync(main);
                else if (args.Contains("--navigation-continuity")) await NavigationContinuityRegression.RunAsync(main, args.Contains("--maximized"));
                else if (args.Contains("--theme-background")) await ThemeBackgroundRegression.RunAsync(main);
                else if (args.Contains("--ui")) await UiRegression.RunAsync(main);
                else if (args.Contains("--settings")) await SettingsRegression.RunAsync(main);
                else if (args.Contains("--live-card-reference")) await LiveCardReferenceAsync(main, args);
                else if (args.Contains("--live-frames")) await LiveFramesAsync(main, args);
                else if (args.Contains("--live-reference")) await LiveReferenceAsync(main, args);
                else if (args.Contains("--live-capture")) await LiveCaptureAsync(main, args);
                else if (args.Contains("--inspect-live")) await InspectLiveAsync(main, args);
                else if (args.Contains("--no-scroll")) await NoScrollAsync(main);
                else if (args.Contains("--explorer")) await ExplorerAsync(main, args.Contains("--move-away"), args.Contains("--move-midway"));
                else if (args.Contains("--lifecycle")) await LifecycleAsync(main);
                else if (args.Contains("--edge")) await EdgeAsync(main);
                else await OrdinaryAsync(main);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); exitCode = 1; }
            finally
            {
                foreach (Form child in Application.OpenForms.Cast<Form>().Where(f => f != main).ToArray()) child.Close();
                main.Close();
            }
        };
        Application.Run(main);
        return exitCode;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;

    private static async Task SetLiveScrollAsync(UIA.ScrollPattern scroll, double percent)
    {
        for (int i = 0; i < 10; i++)
        {
            scroll.SetScrollPercent(UIA.ScrollPattern.NoScroll, percent);
            await Task.Delay(200);
            if (Math.Abs(scroll.Current.VerticalScrollPercent - percent) < 0.04) return;
        }
        throw new InvalidOperationException($"UIA scroll position did not settle at {percent}% (actual {scroll.Current.VerticalScrollPercent}%)");
    }

    private static async Task ClickCaptureButtonAsync(MainForm main, string kind)
    {
        main.TopMost = true; main.ForceActivate();
        Button button = (Button)main.Controls.Find(kind + "Button", true).Single();
        Point point = button.PointToScreen(new Point(button.Width / 2, button.Height / 2));
        InputHelpers.SendMouseMove(point);
        await Task.Delay(150);
        Check(InputHelpers.SendMouseClick(point), "physical mouse click reaches capture button");
        main.TopMost = false;
    }

    private static async Task InspectLiveAsync(MainForm main, string[] args)
    {
        int at = Array.IndexOf(args, "--target-hwnd");
        IntPtr handle = new(long.Parse(args[at + 1]));
        WindowInfo target = new(handle);
        main.Hide(); target.Activate(); await Task.Delay(400);
        UIA.AutomationElement root = UIA.AutomationElement.FromHandle(handle);
        foreach (UIA.AutomationElement element in root.FindAll(UIA.TreeScope.Descendants,
            new UIA.PropertyCondition(UIA.AutomationElement.IsScrollPatternAvailableProperty, true)))
        {
            var pattern = (UIA.ScrollPattern)element.GetCurrentPattern(UIA.ScrollPattern.Pattern);
            Console.WriteLine($"SCROLL: {element.Current.ControlType.ProgrammaticName}; class={element.Current.ClassName}; name={element.Current.Name}; rect={element.Current.BoundingRectangle}; vertical={pattern.Current.VerticallyScrollable}; percent={pattern.Current.VerticalScrollPercent}; view={pattern.Current.VerticalViewSize}");
        }
        foreach (var point in new[] { new System.Windows.Point(2750, 1000), new System.Windows.Point(800, 1000), new System.Windows.Point(190, 1000) })
        {
            Console.WriteLine("POINT: " + point);
            UIA.AutomationElement? element = UIA.AutomationElement.FromPoint(point);
            for (int i = 0; element != null && i < 10; i++, element = UIA.TreeWalker.ControlViewWalker.GetParent(element))
                Console.WriteLine($"ANCESTOR: {element.Current.ControlType.ProgrammaticName}; class={element.Current.ClassName}; name={element.Current.Name}; rect={element.Current.BoundingRectangle}; scroll={element.TryGetCurrentPattern(UIA.ScrollPattern.Pattern, out _)}");
        }
        foreach (UIA.AutomationElement link in root.FindAll(UIA.TreeScope.Descendants,
            new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.Hyperlink)))
        {
            Console.WriteLine($"LINK: class={link.Current.ClassName}; rect={link.Current.BoundingRectangle}; name={link.Current.Name}");
            UIA.AutomationElement? element = link;
            for (int i = 0; element != null && i < 4; i++, element = UIA.TreeWalker.ControlViewWalker.GetParent(element))
                Console.WriteLine($"LINKPARENT: type={element.Current.ControlType.ProgrammaticName}; class={element.Current.ClassName}; rect={element.Current.BoundingRectangle}");
        }
    }

    private static async Task LiveCaptureAsync(MainForm main, string[] args)
    {
        int at = Array.IndexOf(args, "--target-hwnd");
        WindowInfo target = new(new IntPtr(long.Parse(args[at + 1])));
        UIA.AutomationElement root = UIA.AutomationElement.FromHandle(target.Handle);
        UIA.AutomationElement pane = root.FindAll(UIA.TreeScope.Descendants,
            new UIA.PropertyCondition(UIA.AutomationElement.IsScrollPatternAvailableProperty, true)).Cast<UIA.AutomationElement>()
            .Where(e => e.Current.ControlType == UIA.ControlType.Document && e.Current.BoundingRectangle.Width > 500)
            .First(e => ((UIA.ScrollPattern)e.GetCurrentPattern(UIA.ScrollPattern.Pattern)).Current.VerticallyScrollable);
        UIA.ScrollPattern scroll = (UIA.ScrollPattern)pane.GetCurrentPattern(UIA.ScrollPattern.Pattern);
        double original = scroll.Current.VerticalScrollPercent;
        var box = pane.Current.BoundingRectangle;
        Point point = new((int)(box.Left + box.Width * 0.95), (int)(box.Top + box.Height * 0.45));
        try
        {
            await SetLiveScrollAsync(scroll, 0);
            await Task.Delay(400);
            Task select = SelectLiveAsync(point);
            await ClickCaptureButtonAsync(main, "scroll");
            await select;
            await Task.Delay(400);
            ScrollingCaptureForm form = Application.OpenForms.OfType<ScrollingCaptureForm>().Single();
            object manager = Field<object>(form, "manager");
            Rectangle area = Field<Rectangle>(manager, "selectedRectangle");
            Check(Field<WindowInfo>(manager, "selectedWindow").Handle == target.Handle, "live capture selects the authorized browser window");
            Console.WriteLine($"Selected={area}; actual scroll document={box}; progress={scroll.Current.VerticalScrollPercent:F3}%");
            Stopwatch timer = Stopwatch.StartNew();
            while ((bool)manager.GetType().GetProperty("IsCapturing")!.GetValue(manager)!)
            {
                if (timer.Elapsed.TotalSeconds > 80) { await main.CaptureAsync("scroll"); throw new TimeoutException("Live capture exceeded 80 seconds"); }
                await Task.Delay(200);
            }
            await Task.Delay(300);
            Bitmap? bitmap = Field<Bitmap?>(main, "result");
            double final = scroll.Current.VerticalScrollPercent;
            string suffix = args.Contains("--before") ? "before" : "after";
            string label = "live-chapters";
            int labelAt = Array.IndexOf(args, "--label");
            if (labelAt >= 0)
            {
                label = args[labelAt + 1];
                Check(System.Text.RegularExpressions.Regex.IsMatch(label, @"^[a-z0-9-]+$"), "artifact label is a local filename");
            }
            bitmap?.Save($"artifacts/{label}-{suffix}.png", ImageFormat.Png);
            Console.WriteLine($"Final={final:F6}%; result={bitmap?.Width}x{bitmap?.Height}; status={Field<object>(manager, "status")}; seconds={timer.Elapsed.TotalSeconds:F1}");
            Check(final >= 99.9, "live chapter document reaches bottom");
            Check(area.Left >= box.Left - 2 && area.Top >= box.Top - 2 && area.Right <= box.Right + 2 && area.Bottom <= box.Bottom + 2,
                "capture excludes fixed navigation and outer document");
            target.Activate(); await Task.Delay(400);
            using Bitmap current = new Screenshot().CaptureRectangle(area);
            int height = Math.Min(300, area.Height / 3);
            using Bitmap resultTail = bitmap!.Clone(new Rectangle(20, bitmap.Height - height, area.Width - 40, height), PixelFormat.Format32bppArgb);
            using Bitmap screenTail = current.Clone(new Rectangle(20, area.Height - height, area.Width - 40, height), PixelFormat.Format32bppArgb);
            Check(ImageHelpers.CompareImages(resultTail, screenTail), "live chapter image retains the final visible rows");
            form.Close();
        }
        finally
        {
            await SetLiveScrollAsync(scroll, original);
            target.Activate();
        }
    }

    private static async Task LiveCardReferenceAsync(MainForm main, string[] args)
    {
        int at = Array.IndexOf(args, "--target-hwnd");
        WindowInfo target = new(new IntPtr(long.Parse(args[at + 1])));
        var pane = UIA.AutomationElement.FromHandle(target.Handle).FindAll(UIA.TreeScope.Descendants,
            new UIA.PropertyCondition(UIA.AutomationElement.IsScrollPatternAvailableProperty, true)).Cast<UIA.AutomationElement>()
            .First(e => e.Current.ControlType == UIA.ControlType.Document && e.Current.BoundingRectangle.Width > 500 &&
                ((UIA.ScrollPattern)e.GetCurrentPattern(UIA.ScrollPattern.Pattern)).Current.VerticallyScrollable);
        var scroll = (UIA.ScrollPattern)pane.GetCurrentPattern(UIA.ScrollPattern.Pattern);
        double original = scroll.Current.VerticalScrollPercent;
        string folder = "artifacts/live-card-reference";
        Directory.CreateDirectory(folder);
        main.Hide(); target.Activate();
        try
        {
            await SetLiveScrollAsync(scroll, 0);
            InputHelpers.SendMouseMove(500, 200);
            await Task.Delay(400);
            var content = pane.FindFirst(UIA.TreeScope.Descendants, new UIA.PropertyCondition(UIA.AutomationElement.ClassNameProperty, "course-list-con"));
            var links = content.FindAll(UIA.TreeScope.Descendants, new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.Hyperlink))
                .Cast<UIA.AutomationElement>().Where(e => e.Current.ClassName == "color1" ||
                    (e.Current.ClassName.Length == 0 && e.Current.BoundingRectangle.Width >= 400 && e.Current.BoundingRectangle.Height >= 200))
                .OrderBy(e => e.Current.BoundingRectangle.Top).ThenBy(e => e.Current.BoundingRectangle.Left).ToArray();
            Check(links.Length > 0 && links.Length % 2 == 0, "course cards expose both covers and titles");
            double paneTop = pane.Current.BoundingRectangle.Top;
            double[] ys = links.Select(e => e.Current.BoundingRectangle.Top - paneTop).ToArray();
            var rows = new List<object>();
            var captured = new HashSet<int>();
            for (int percent = 0; percent <= 100; percent += 5)
            {
                await SetLiveScrollAsync(scroll, percent);
                await Task.Delay(400);
                var box = pane.Current.BoundingRectangle;
                Rectangle viewport = Rectangle.FromLTRB((int)box.Left, (int)box.Top, (int)box.Right, (int)box.Bottom);
                using Bitmap screen = new Screenshot().CaptureRectangle(viewport);
                for (int i = 0; i < links.Length; i++)
                {
                    if (captured.Contains(i)) continue;
                    var b = links[i].Current.BoundingRectangle;
                    // Upper viewport avoids the site's fixed help buttons. No links are invoked.
                    if (b.Top < box.Top + 8 || b.Bottom > box.Top + Math.Min(800, box.Height - 8)) continue;
                    Rectangle rect = Rectangle.FromLTRB((int)Math.Ceiling(b.Left) - viewport.Left, (int)Math.Ceiling(b.Top) - viewport.Top,
                        (int)Math.Floor(b.Right) - viewport.Left, (int)Math.Floor(b.Bottom) - viewport.Top);
                    if (!new Rectangle(Point.Empty, screen.Size).Contains(rect)) continue;
                    using Bitmap item = screen.Clone(rect, PixelFormat.Format32bppArgb);
                    string file = $"item-{i + 1:D2}.png";
                    item.Save(Path.Combine(folder, file), ImageFormat.Png);
                    rows.Add(new { order = i, file, x = rect.X, y = ys[i], width = rect.Width, height = rect.Height,
                        kind = links[i].Current.ClassName == "color1" ? "title" : "cover" });
                    captured.Add(i);
                }
                Console.WriteLine($"Reference request={percent}%; actual={scroll.Current.VerticalScrollPercent:F3}%; captured={captured.Count}; last={links[^1].Current.BoundingRectangle}");
            }
            File.WriteAllText(Path.Combine(folder, "items.json"), System.Text.Json.JsonSerializer.Serialize(rows));
            Console.WriteLine("Missing: " + string.Join(",", Enumerable.Range(0, links.Length).Where(i => !captured.Contains(i))));
            Check(captured.Count == links.Length, $"independent screenshots cover all {links.Length / 2} course covers and titles");
        }
        finally { await SetLiveScrollAsync(scroll, original); target.Activate(); }
    }

    private static async Task LiveFramesAsync(MainForm main, string[] args)
    {
        int at = Array.IndexOf(args, "--target-hwnd");
        WindowInfo target = new(new IntPtr(long.Parse(args[at + 1])));
        var pane = UIA.AutomationElement.FromHandle(target.Handle).FindAll(UIA.TreeScope.Descendants,
            new UIA.PropertyCondition(UIA.AutomationElement.IsScrollPatternAvailableProperty, true)).Cast<UIA.AutomationElement>()
            .First(e => e.Current.ControlType == UIA.ControlType.Document && e.Current.BoundingRectangle.Width > 500 &&
                ((UIA.ScrollPattern)e.GetCurrentPattern(UIA.ScrollPattern.Pattern)).Current.VerticallyScrollable);
        var scroll = (UIA.ScrollPattern)pane.GetCurrentPattern(UIA.ScrollPattern.Pattern);
        double original = scroll.Current.VerticalScrollPercent;
        var box = pane.Current.BoundingRectangle;
        Rectangle area = Rectangle.FromLTRB((int)box.Left, (int)box.Top, (int)box.Right, (int)box.Bottom);
        main.Hide(); target.Activate();
        try
        {
            await SetLiveScrollAsync(scroll, 0);
            for (int i = 0; i < 4; i++)
            {
                InputHelpers.SendMouseMove(area.Right - 40, area.Top + area.Height / 2);
                await Task.Delay(600);
                using Bitmap frame = new Screenshot().CaptureRectangle(area);
                frame.Save($"artifacts/live-space-frame-{i}.png", ImageFormat.Png);
                Console.WriteLine($"Frame={i}; percent={scroll.Current.VerticalScrollPercent}; view={scroll.Current.VerticalViewSize}; size={area.Size}");
                InputHelpers.SendMouseWheel(-240);
            }
        }
        finally { await SetLiveScrollAsync(scroll, original); target.Activate(); }
    }

    // Reads only the currently authorized chapter index. It scrolls, never invokes links.
    private static async Task LiveReferenceAsync(MainForm main, string[] args)
    {
        int at = Array.IndexOf(args, "--target-hwnd");
        WindowInfo target = new(new IntPtr(long.Parse(args[at + 1])));
        UIA.AutomationElement root = UIA.AutomationElement.FromHandle(target.Handle);
        UIA.AutomationElement pane = root.FindAll(UIA.TreeScope.Descendants,
            new UIA.PropertyCondition(UIA.AutomationElement.IsScrollPatternAvailableProperty, true)).Cast<UIA.AutomationElement>()
            .First(e => e.Current.ControlType == UIA.ControlType.Document && e.Current.BoundingRectangle.Width > 500 &&
                ((UIA.ScrollPattern)e.GetCurrentPattern(UIA.ScrollPattern.Pattern)).Current.VerticallyScrollable);
        var scroll = (UIA.ScrollPattern)pane.GetCurrentPattern(UIA.ScrollPattern.Pattern);
        double original = scroll.Current.VerticalScrollPercent;
        var links = pane.FindAll(UIA.TreeScope.Descendants, new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.Hyperlink))
            .Cast<UIA.AutomationElement>().Where(e => System.Text.RegularExpressions.Regex.IsMatch(e.Current.Name, @"^\d+\.\d+\s")).ToArray();
        Check(links.Length > 0, "live index exposes numbered chapter links");
        string folder = "artifacts/live-chapter-reference";
        Directory.CreateDirectory(folder);
        var rows = new List<object>();
        var captured = new HashSet<int>();
        try
        {
            main.Hide(); target.Activate();
            InputHelpers.SendMouseMove(500, 200); // Outside the inner document: no hovered chapter.
            foreach (double percent in new double[] { 0, 15, 30, 45, 60, 75, 90, 100 })
            {
                await SetLiveScrollAsync(scroll, percent);
                await Task.Delay(400);
                var box = pane.Current.BoundingRectangle;
                Rectangle viewport = Rectangle.FromLTRB((int)box.Left, (int)box.Top, (int)box.Right, (int)box.Bottom);
                using Bitmap screen = new Screenshot().CaptureRectangle(viewport);
                for (int i = 0; i < links.Length; i++)
                {
                    if (captured.Contains(i)) continue;
                    var bounds = links[i].Current.BoundingRectangle;
                    if (bounds.IsEmpty || bounds.Top < box.Top + 2 || bounds.Bottom > box.Bottom - 2 || bounds.Height < 10) continue;
                    Rectangle rectangle = Rectangle.FromLTRB((int)Math.Ceiling(bounds.Left) - viewport.Left,
                        (int)Math.Ceiling(bounds.Top) - viewport.Top, (int)Math.Floor(bounds.Right) - viewport.Left,
                        (int)Math.Floor(bounds.Bottom) - viewport.Top);
                    if (!new Rectangle(Point.Empty, screen.Size).Contains(rectangle)) continue;
                    using Bitmap row = screen.Clone(rectangle, PixelFormat.Format32bppArgb);
                    string file = $"row-{i + 1:D2}.png";
                    row.Save(Path.Combine(folder, file), ImageFormat.Png);
                    string id = System.Text.RegularExpressions.Regex.Match(links[i].Current.Name, @"^\d+\.\d+").Value;
                    rows.Add(new { order = i, id, file, x = rectangle.X, width = rectangle.Width, height = rectangle.Height });
                    captured.Add(i);
                }
            }
            Check(captured.Count == links.Length, $"independent visible screenshots cover all {links.Length} chapter rows");
            File.WriteAllText(Path.Combine(folder, "rows.json"), System.Text.Json.JsonSerializer.Serialize(rows));
        }
        finally { await SetLiveScrollAsync(scroll, original); target.Activate(); }
    }

    private static async Task SelectLiveAsync(Point point)
    {
        for (int attempt = 0; !Application.OpenForms.OfType<RegionCaptureForm>().Any(); attempt++)
        {
            if (attempt > 50) throw new TimeoutException("Region selector did not open; refusing to click the live page");
            await Task.Delay(100);
        }
        await Task.Delay(700);
        InputHelpers.SendMouseMove(point);
        await Task.Delay(500);
        Check(InputHelpers.SendMouseClick(point), "selection click sent while the overlay is open");
    }

    private static async Task EdgeAsync(MainForm main)
    {
        WindowInfo target = new WindowsList().GetVisibleWindowsList().First(x => x.Text.Contains("CaptureOnly Edge 长文章"));
        bool topmost = target.TopMost;
        target.TopMost = true;
        try
        {
            target.Restore(); target.BringToFront(); target.Activate();
            await Task.Delay(500);
            Rectangle bounds = CaptureHelpers.GetWindowRectangle(target.Handle);
            Point point = new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            Task select = Task.Run(async () =>
            {
                await Task.Delay(1600);
                InputHelpers.SendMouseMove(point);
                await Task.Delay(1000);
                using (Bitmap overlay = new Screenshot().CaptureFullscreen()) overlay.Save("artifacts/stable-selector.png", ImageFormat.Png);
                InputHelpers.SendMouseClick(point);
            });

            // Exercise the actual main button, released scrolling form, region selector and manager together.
            ((Button)main.Controls.Find("scrollButton", true).Single()).PerformClick();
            await select;
            await Task.Delay(450);
            ScrollingCaptureForm form = Application.OpenForms.OfType<ScrollingCaptureForm>().Single();
            object manager = Field<object>(form, "manager");
            Rectangle area = Field<Rectangle>(manager, "selectedRectangle");
            WindowInfo selected = Field<WindowInfo>(manager, "selectedWindow");
            Console.WriteLine($"Window={bounds}; selected={area}; handle={selected.Handle}; target={target.Handle}");
            Check(selected.Handle == target.Handle, "main button one-click selection targets Edge");
            Check(area.Top > bounds.Top + 70 && area.Height < bounds.Height, "one-click selection excludes browser toolbar");
            Stopwatch timer = Stopwatch.StartNew();
            while ((bool)manager.GetType().GetProperty("IsCapturing")!.GetValue(manager)!)
            {
                if (timer.Elapsed > TimeSpan.FromSeconds(110)) { await form.StartStopScrollingCapture(); throw new TimeoutException("capture exceeded 110 seconds"); }
                await Task.Delay(250);
            }
            await Task.Delay(400);
            Bitmap bitmap = Field<Bitmap>(main, "result");
            Check(bitmap != null, "released scrolling form returns image");
            Console.WriteLine($"Result={bitmap!.Width}x{bitmap.Height}; status={Field<object>(manager, "status")}; seconds={timer.Elapsed.TotalSeconds:0.0}");
            bitmap.Save("artifacts/stable-edge.png", ImageFormat.Png);
            Check(bitmap.Height > area.Height * 4, "actual UI flow captures multiple article screens");
            target.BringToFront(); target.Activate();
            await Task.Delay(500);
            using Bitmap final = new Screenshot().CaptureRectangle(area);
            final.Save("artifacts/stable-edge-final.png", ImageFormat.Png);
            Rectangle tail = new(20, 0, area.Width - 40, 350);
            using Bitmap resultTail = bitmap.Clone(new Rectangle(tail.X, bitmap.Height - tail.Height, tail.Width, tail.Height), PixelFormat.Format32bppArgb);
            using Bitmap screenTail = final.Clone(new Rectangle(tail.X, final.Height - tail.Height, tail.Width, tail.Height), PixelFormat.Format32bppArgb);
            Check(ImageHelpers.CompareImages(resultTail, screenTail), "captured article tail equals final visible content");
            form.Close();
            Check(main.Visible, "closing scrolling preview restores main window");
        }
        finally { target.TopMost = topmost; }
    }

    private static async Task OrdinaryAsync(MainForm main)
    {
        Check(main.Text == "SnapFlow", "application name is SnapFlow");
        main.TopMost = true; main.ForceActivate();
        await Task.Delay(500);
        using (Bitmap initial = new Screenshot().CaptureWindow(main.Handle)) initial.Save("artifacts/stable-main-initial.png", ImageFormat.Png);
        ((Button)main.Controls.Find("fullButton", true).Single()).PerformClick();
        await Task.Delay(1000);
        Bitmap result = Field<Bitmap>(main, "result");
        Check(main.Visible && result.Width > 1000 && result.Height > 700, "ordinary capture button returns a full desktop image");
        string path = "artifacts/stable-ordinary.png";
        Check(ImageHelpers.SaveImage(result, path), "original PNG saver works");
        using Bitmap loaded = new(path);
        Check(ImageHelpers.CompareImages(result, loaded), "saved PNG matches captured pixels");
        main.TopMost = true; main.ForceActivate();
        await Task.Delay(500);
        using Bitmap window = new Screenshot().CaptureWindow(main.Handle);
        window.Save("artifacts/stable-main.png", ImageFormat.Png);
    }

    private static async Task<WindowInfo> OpenExplorerTargetAsync()
    {
        string folder = Path.GetFullPath("artifacts/ShareX-roll-Explorer-test");
        Directory.CreateDirectory(folder);
        for (int i = 1; i <= 180; i++)
        {
            string file = Path.Combine(folder, $"{i:D3}-滚动截图测试-文件必须按顺序保留.txt");
            if (!File.Exists(file)) File.WriteAllText(file, $"Explorer scrolling regression item {i}");
        }
        WindowInfo? target = new WindowsList().GetVisibleWindowsList().FirstOrDefault(x => x.Text.Contains("ShareX-roll-Explorer-test"));
        if (target == null) Process.Start(new ProcessStartInfo("explorer.exe", $"/n,\"{folder}\"") { UseShellExecute = true });
        for (int i = 0; i < 32 && target == null; i++)
        {
            await Task.Delay(250);
            target = new WindowsList().GetVisibleWindowsList().FirstOrDefault(x => x.Text.Contains("ShareX-roll-Explorer-test"));
        }
        Check(target != null, "opened isolated Explorer test directory");
        return target!;
    }

    private static async Task ExplorerAsync(MainForm main, bool moveAway, bool moveMidway)
    {
        WindowInfo target = await OpenExplorerTargetAsync();
        target.Restore(); target.Activate();
        await Task.Delay(600);
        InputHelpers.SendKeyPressModifiers((VirtualKeyCode)0x36, VirtualKeyCode.CONTROL, VirtualKeyCode.SHIFT);
        await Task.Delay(1000);
        UIA.AutomationElement root = UIA.AutomationElement.FromHandle(target.Handle);
        UIA.AutomationElement? pane = null;
        UIA.ScrollPattern? scroll = null;
        foreach (UIA.AutomationElement element in root.FindAll(UIA.TreeScope.Descendants,
            new UIA.PropertyCondition(UIA.AutomationElement.IsScrollPatternAvailableProperty, true)))
        {
            Console.WriteLine($"Scroll candidate: {element.Current.Name}; {element.Current.ControlType.ProgrammaticName}; {element.Current.BoundingRectangle}");
            if (element.GetCurrentPattern(UIA.ScrollPattern.Pattern) is UIA.ScrollPattern candidate && candidate.Current.VerticallyScrollable && element.Current.ControlType == UIA.ControlType.List)
            {
                pane = element; scroll = candidate; break;
            }
        }
        Check(scroll != null && pane != null, "Explorer list exposes a vertical scroll range");
        scroll!.SetScrollPercent(UIA.ScrollPattern.NoScroll, 0);
        await Task.Delay(400);
        var paneBounds = pane!.Current.BoundingRectangle;
        Point point = new((int)(paneBounds.Left + paneBounds.Width / 2), (int)(paneBounds.Top + paneBounds.Height / 2));
        double before = scroll.Current.VerticalScrollPercent;
        Rectangle targetBounds = CaptureHelpers.GetWindowRectangle(target.Handle);
        Console.WriteLine($"Explorer bounds={targetBounds}; list={paneBounds}; percent before={before}; foreground={NativeMethods.GetForegroundWindowText()}");
        main.Show(); main.ForceActivate();
        await Task.Delay(500);
        Task select = Task.Run(async () =>
        {
            await Task.Delay(1700);
            InputHelpers.SendMouseMove(point);
            await Task.Delay(600);
            InputHelpers.SendMouseClick(point);
            if (moveAway || moveMidway)
            {
                await Task.Delay(moveMidway ? 2000 : 25);
                InputHelpers.SendMouseMove(3050, 200);
            }
        });
        ((Button)main.Controls.Find("scrollButton", true).Single()).PerformClick();
        await select; await Task.Delay(550);
        ScrollingCaptureForm form = Application.OpenForms.OfType<ScrollingCaptureForm>().Single();
        object manager = Field<object>(form, "manager");
        Rectangle area = Field<Rectangle>(manager, "selectedRectangle");
        Console.WriteLine($"Selected={area}; target={target.Handle}; selectedWindow={Field<WindowInfo>(manager, "selectedWindow").Handle}; foreground={NativeMethods.GetForegroundWindowText()}; cursor={Cursor.Position}; scroll after first input={scroll.Current.VerticalScrollPercent}");
        Stopwatch timer = Stopwatch.StartNew();
        while ((bool)manager.GetType().GetProperty("IsCapturing")!.GetValue(manager)!)
        {
            if (timer.Elapsed.TotalSeconds > 75) { await form.StartStopScrollingCapture(); break; }
            await Task.Delay(250);
        }
        await Task.Delay(450);
        double after = scroll.Current.VerticalScrollPercent;
        Bitmap? bitmap = Field<Bitmap?>(main, "result");
        Console.WriteLine($"Explorer after={after}; result={bitmap?.Width}x{bitmap?.Height}; status={Field<object>(manager, "status")}; seconds={timer.Elapsed.TotalSeconds:F1}");
        if (bitmap != null) bitmap.Save(moveAway ? "artifacts/explorer-away.png" : moveMidway ? "artifacts/explorer-midway.png" : "artifacts/explorer-still.png", ImageFormat.Png);
        Check(after > before + 50, "actual Explorer list scrolls beyond halfway");
        Check(after >= 99, "actual Explorer list reaches bottom");
        Check(bitmap != null && bitmap.Height > area.Height * 2, "Explorer capture contains multiple screens");
        target.Activate(); await Task.Delay(400);
        using Bitmap final = new Screenshot().CaptureRectangle(area);
        using Bitmap resultTail = bitmap!.Clone(new Rectangle(20, bitmap.Height - 300, area.Width - 40, 300), PixelFormat.Format32bppArgb);
        using Bitmap screenTail = final.Clone(new Rectangle(20, area.Height - 300, area.Width - 40, 300), PixelFormat.Format32bppArgb);
        Check(ImageHelpers.CompareImages(resultTail, screenTail), "Explorer capture retains final file-list rows pixel-for-pixel");
        form.Close();
        // Leave the isolated test folder window open for diagnosis; never close unrelated Explorer windows.
    }

    private static async Task NoScrollAsync(MainForm main)
    {
        using Form target = new() { Text = "ShareX-roll-no-scroll-fixture", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(300, 300, 1200, 800) };
        target.Controls.Add(new Label { Text = "此窗口没有可滚动内容", AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
        target.TopMost = true; target.Show(); target.Activate(); await Task.Delay(300);
        Point point = new(target.Left + target.Width / 2, target.Top + target.Height / 2);
        main.Show(); main.ForceActivate(); await Task.Delay(300);
        Task select = Task.Run(async () => { await Task.Delay(1400); InputHelpers.SendMouseMove(point); await Task.Delay(500); InputHelpers.SendMouseClick(point); });
        ((Button)main.Controls.Find("scrollButton", true).Single()).PerformClick();
        await select;
        ScrollingCaptureForm form = Application.OpenForms.OfType<ScrollingCaptureForm>().Single();
        object manager = Field<object>(form, "manager");
        Check(Field<WindowInfo>(manager, "selectedWindow").Handle == target.Handle, "no-scroll test selects its static fixture, not another window");
        // Selection can spend up to 1.5 s querying UI Automation before capture and its three retries begin.
        // Wait for completion with a deadline instead of assuming the preceding 1.8 s covers both stages.
        Stopwatch completion = Stopwatch.StartNew();
        while ((bool)manager.GetType().GetProperty("IsCapturing")!.GetValue(manager)! && completion.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(100);
        await Task.Delay(100); // Let the completion continuation refresh the preview and its title.
        Check(!(bool)manager.GetType().GetProperty("IsCapturing")!.GetValue(manager)!, "non-scrollable target ends after bounded retries");
        Check((ScrollingCaptureStatus)Field<object>(manager, "status") == ScrollingCaptureStatus.Failed, "zero movement is not reported as successful");
        Check(main.Visible && form.IsDisposed && Field<Label>(main, "status").Text.Contains("未检测到滚动"), "main preview visibly explains that only one screen was captured");
        target.Hide();
        using Bitmap preview = new Screenshot().CaptureWindow(main.Handle);
        preview.Save("artifacts/no-scroll-warning.png", ImageFormat.Png);
        form.Close();
    }

    private static async Task LifecycleAsync(MainForm main)
    {
        WindowInfo target = await OpenExplorerTargetAsync();
        Task cancel = Task.Run(async () => { await Task.Delay(1500); InputHelpers.SendKeyPress(VirtualKeyCode.ESCAPE); });
        await ClickCaptureButtonAsync(main, "scroll");
        await cancel; await Task.Delay(600);
        Check(!Application.OpenForms.OfType<RegionCaptureForm>().Any(), "Escape closes the actual selection overlay");
        Check(!Application.OpenForms.OfType<ScrollingCaptureForm>().Any(), "Escape cancels selection and disposes the capture window");
        Check(main.Visible, "main window restored after cancelled selection");

        target.Restore(); target.BringToFront(); target.Activate();
        UIA.AutomationElement root = UIA.AutomationElement.FromHandle(target.Handle);
        UIA.AutomationElement list = root.FindFirst(UIA.TreeScope.Descendants,
            new UIA.AndCondition(new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.List),
                new UIA.PropertyCondition(UIA.AutomationElement.IsScrollPatternAvailableProperty, true)));
        ((UIA.ScrollPattern)list.GetCurrentPattern(UIA.ScrollPattern.Pattern)).SetScrollPercent(UIA.ScrollPattern.NoScroll, 0);
        await Task.Delay(300);
        var rect = list.Current.BoundingRectangle;
        Rectangle bounds = new((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
        Point point = new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        Task select = Task.Run(async () => { await Task.Delay(1400); InputHelpers.SendMouseMove(point); await Task.Delay(500); InputHelpers.SendMouseClick(point); });
        await ClickCaptureButtonAsync(main, "scroll");
        await select; await Task.Delay(700);
        ScrollingCaptureForm active = Application.OpenForms.OfType<ScrollingCaptureForm>().Single();
        object manager = Field<object>(active, "manager");
        Check((bool)manager.GetType().GetProperty("IsCapturing")!.GetValue(manager)!, "capture can start again after cancellation");
        List<HotkeyInfo> keys = Field<List<HotkeyInfo>>(main, "hotkeys");
        Check(keys.Any(k => k.KeyCode == Keys.S && k.Status == HotkeyStatus.Registered), "scrolling stop shortcut registered, with fallback if needed");
        await main.CaptureAsync("scroll");
        await Task.Delay(700);
        Check(!(bool)manager.GetType().GetProperty("IsCapturing")!.GetValue(manager)!, "same shortcut handler stops active capture");
        Check(active.IsDisposed && Field<Bitmap>(main, "result") != null, "stop transfers the image and disposes the capture window");
        active.Close();
        Check(main.Visible, "main window restored after stopped capture");
    }
}
