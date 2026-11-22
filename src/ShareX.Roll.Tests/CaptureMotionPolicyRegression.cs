using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Diagnostics;
using System.Reflection;

internal static class CaptureMotionPolicyRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    internal static async Task RunAsync(MainForm main, SoftwareStyle style = SoftwareStyle.Default)
    {
        MethodInfo clone = typeof(RegionCaptureTasks).GetMethod("GetRegionCaptureOptions", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (bool enabled in new[] { false, true })
        {
            RegionCaptureOptions source = new() { EnableAnimations = enabled, DetectControls = false, MagnifierPixelCount = 17 };
            RegionCaptureOptions copy = (RegionCaptureOptions)clone.Invoke(null, new object[] { source })!;
            Check(copy.EnableAnimations == enabled, $"selection options preserve animation policy={enabled}");
            Check(!ReferenceEquals(copy, source) && !copy.DetectControls && copy.MagnifierPixelCount == 17,
                "motion policy preserves the existing independent selection options copy");
        }
        Check(((RegionCaptureOptions)clone.Invoke(null, new object?[] { null })!).EnableAnimations,
            "other capture hosts retain their default selection animation policy");

        bool original = UiMotion.UserEnabled;
        ApplicationPreferences preferences = (ApplicationPreferences)typeof(MainForm).GetField("preferences", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        SoftwareStyle originalStyle = preferences.Style;
        bool themed = style != SoftwareStyle.Default;
        MethodInfo apply = typeof(MainForm).GetMethod("ApplyAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            if (themed) { preferences.Style = style; apply.Invoke(main, null); }
            CapturePalette palette = CapturePalette.For(preferences.Appearance, preferences.Style);
            foreach (bool enabled in new[] { false, true })
            foreach (string kind in new[] { "region", "window", "scroll" })
            {
                UiMotion.UserEnabled = enabled;
                bool expected = UiMotion.Enabled;
                bool observed = false;
                bool? actual = null;
                Color overlayText = Color.Empty, overlaySurface = Color.Empty, scrollingSurface = Color.Empty;
                SynchronizationContext context = SynchronizationContext.Current!;
                bool finished = false;
                int pending = 0;
                Stopwatch timeout = Stopwatch.StartNew();
                void Observe()
                {
                    Interlocked.Exchange(ref pending, 0);
                    if (finished) return;
                    RegionCaptureForm? selector = Application.OpenForms.OfType<RegionCaptureForm>().FirstOrDefault();
                    if (selector != null)
                    {
                        actual = selector.Options.EnableAnimations;
                        if (themed)
                        {
                            const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
                            overlayText = (Color)typeof(RegionCaptureForm).GetField("textColor", Members)!.GetValue(selector)!;
                            overlaySurface = (Color)typeof(RegionCaptureForm).GetField("textBackgroundColor", Members)!.GetValue(selector)!;
                            scrollingSurface = Application.OpenForms.OfType<ScrollingCaptureForm>().FirstOrDefault()?.BackColor ?? Color.Empty;
                        }
                        observed = true;
                        finished = true;
                        selector.Close(); // Cancel without capturing or altering the clipboard.
                    }
                    else if (timeout.ElapsedMilliseconds > 8000)
                    {
                        finished = true;
                        foreach (ScrollingCaptureForm form in Application.OpenForms.OfType<ScrollingCaptureForm>().ToArray()) form.Close();
                    }
                }
                // The selector continuously invalidates its canvas. WM_TIMER is lower
                // priority than WM_PAINT; post observation rather than starving a timer.
                using System.Threading.Timer observer = new(_ =>
                {
                    if (Interlocked.CompareExchange(ref pending, 1, 0) == 0) context.Post(_ => Observe(), null);
                }, null, 20, 20);
                await main.CaptureAsync(kind);
                finished = true;
                Check(observed && actual == expected, $"{kind} actual selector honors effective animation policy={expected}");
                if (themed)
                    Check(overlayText == palette.Text && (overlaySurface.ToArgb() & 0xffffff) == (palette.Background.ToArgb() & 0xffffff) &&
                        (kind != "scroll" || scrollingSurface == palette.Background), $"{kind} uses {style} surfaces and text in native panels");
                Stopwatch closing = Stopwatch.StartNew();
                while (Application.OpenForms.OfType<ScrollingCaptureForm>().Any() && closing.ElapsedMilliseconds < 2000)
                    await Task.Delay(20);
                Check(!Application.OpenForms.OfType<RegionCaptureForm>().Any() &&
                    !Application.OpenForms.OfType<ScrollingCaptureForm>().Any(), $"{kind} cancelled selector releases capture windows");
                Check(main.Visible, $"{kind} cancelled capture returns to the workspace");
            }
        }
        finally
        {
            if (themed) { preferences.Style = originalStyle; apply.Invoke(main, null); }
            UiMotion.UserEnabled = original;
        }
    }
}
