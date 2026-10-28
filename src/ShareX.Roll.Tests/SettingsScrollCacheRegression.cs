using ShareX.Roll;
using System.Diagnostics;
using System.Reflection;

internal static class SettingsScrollCacheRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static void Call(object owner, string name) => owner.GetType().GetMethod(name, Private)!.Invoke(owner, null);
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void Check(bool condition, string text)
    {
        if (!condition) throw new InvalidOperationException(text);
        Console.WriteLine("PASS: " + text);
    }

    private static async Task Warm(SettingsScrollBackdropCache cache)
    {
        for (int i = 0; i < 160 && !cache.IsReady; i++) await Task.Delay(25);
        Check(cache.IsReady, "idle settings produce a reusable backdrop");
    }

    internal static async Task RunAsync(MainForm main)
    {
        Random random = new(210);
        foreach (int width in new[] { 17, 63, 257 })
        foreach (int rows in new[] { 1, 16, 32 })
        foreach (bool clearAtBottom in new[] { false, true })
        foreach (bool blendAlpha in new[] { false, true })
        {
            int height = rows + 7, top = clearAtBottom ? height - rows : 0;
            int[] material = Enumerable.Range(0, width * height).Select(_ => (int)random.NextInt64(0, 1L << 32)).ToArray();
            int[] sharp = Enumerable.Range(0, width * rows).Select(_ => (int)random.NextInt64(0, 1L << 32)).ToArray();
            int[] expected = (int[])material.Clone();
            for (int row = 0; row < rows; row++)
            {
                double t = rows <= 1 ? 0 : (clearAtBottom ? rows - 1 - row : row) / (double)(rows - 1);
                uint amount = (uint)Math.Round(256 * (1 - t * t * (3 - 2 * t)));
                if (amount == 0) continue;
                for (int x = 0; x < width; x++)
                {
                    int at = (top + row) * width + x;
                    uint a = (uint)material[at], b = (uint)sharp[row * width + x];
                    uint result = blendAlpha ? 0u : 0xFF000000;
                    foreach (int shift in blendAlpha ? new[] { 0, 8, 16, 24 } : new[] { 0, 8, 16 })
                        result |= ((((a >> shift) & 255) * (256 - amount) + ((b >> shift) & 255) * amount) >> 8) << shift;
                    expected[at] = (int)result;
                }
            }
            SettingsBarBackdrop.BlendScrollEdge(material, sharp, width, height, clearAtBottom, blendAlpha);
            Check(material.SequenceEqual(expected), $"gradient {width}x{rows}/{clearAtBottom}/{blendAlpha}: exact clear edge, mirrored smooth fade, unchanged body and SIMD tail");
        }
        foreach (AppearanceMode theme in new[] { AppearanceMode.Light, AppearanceMode.Dark })
        {
            int[] input = Enumerable.Range(0, 1027).Select(_ => (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1)).ToArray();
            int[] actual = new int[input.Length];
            uint matte = BackdropSnapshot.RemovedMatte(CapturePalette.For(theme));
            SettingsBarBackdrop.RemoveMaterialMatte(input, actual, matte);
            Check(input.Select((color, i) => (uint)actual[i] == BackdropSnapshot.RemoveMatte((uint)color | 0xFF000000, matte)).All(same => same),
                theme + ": vectorized backdrop alpha matches the original pixels including saturation and the scalar tail");
        }

        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        Size originalSize = main.Size;
        Point cursor = Cursor.Position;
        try
        {
            Call(main, "ReleaseCaptureHotkeys");
            ApplicationPreferences working = original.Clone();
            working.AnimationsEnabled = working.TransparencyEnabled = true;
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, working);
            Call(main, "ApplyAppearance");
            main.WindowState = FormWindowState.Maximized;
            main.TopMost = true;
            Cursor.Position = Point.Empty;
            Call(main, "OpenSettings");
            await Task.Delay(450);
            SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
            SettingsScrollBackdropCache cache = Field<SettingsScrollBackdropCache>(page, "scrollBackdrop");
            SettingsScrollHost host = Find<SettingsScrollHost>(page, "settingsScrollHost");
            Panel viewport = Field<Panel>(page, "scroller");
            foreach (AppearanceMode theme in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                Find<RadioButton>(page, "theme" + theme).Checked = true;
                await Warm(cache);
                int builds = cache.Builds, hits = cache.Hits;
                int updates = host.ScrollUpdates;
                long ticks = host.ScrollUpdateTicks;
                viewport.AutoScrollPosition = Point.Empty;
                for (int i = 0; i < 6; i++) { host.ScrollWheel(-120); await Task.Delay(12); }
                await Task.Delay(450);
                Check(cache.Builds == builds && cache.Hits > hits + 2,
                    theme + ": wheel frames reuse the backdrop without rebuilding the settings scene");
                Check(!host.IsScrolling && viewport.AutoScrollPosition.Y < 0 && !viewport.HorizontalScroll.Visible,
                    theme + ": cached scrolling retains the native vertical offset and settles exactly");
                Console.WriteLine($"COST {theme}: updates={host.ScrollUpdates - updates}, meanMs={(host.ScrollUpdateTicks - ticks) * 1000d / Stopwatch.Frequency / Math.Max(1, host.ScrollUpdates - updates):F3}");

                await Warm(cache);
                Control repository = Find<Control>(page, "aboutRepository");
                repository.Invalidate();
                using (Bitmap sample = new(16, 16))
                using (Graphics graphics = Graphics.FromImage(sample))
                {
                    Check(cache.Draw(graphics, new Point(0, -64 * page.DeviceDpi / 96), sample.Size),
                        theme + ": local hover damage does not discard unrelated background regions");
                    Point changed = Field<Control>(page, "content").PointToClient(repository.PointToScreen(Point.Empty));
                    Check(!cache.Draw(graphics, changed, sample.Size),
                        theme + ": a damaged region cannot display outdated cached pixels");
                }
                await Warm(cache);

                Label label = Find<Control>(page, "transparencyCaption").Controls.OfType<Label>().First();
                string text = label.Text;
                label.Text += " / cache test";
                Check(!cache.IsReady, theme + ": text changes immediately invalidate the old backdrop");
                await Warm(cache);
                label.Text = text;
                await Warm(cache);
                SettingsDisclosure disclosure = Find<SettingsDisclosure>(page, "scrollingOptionsButton");
                disclosure.PerformClick();
                Check(!cache.IsReady, theme + ": disclosure layout changes cannot use a stale backdrop");
                await Task.Delay(650);
                await Warm(cache);
                disclosure.PerformClick();
                await Task.Delay(650);
                await Warm(cache);
                MotionToggle transparency = Find<MotionToggle>(page, "transparencyToggle");
                transparency.Checked = false;
                Check(!cache.IsReady, theme + ": solid mode immediately stops using the glass cache");
                transparency.Checked = true;
                await Warm(cache);
            }

            main.WindowState = FormWindowState.Normal;
            main.ClientSize = new Size(960 * main.DeviceDpi / 96, 720 * main.DeviceDpi / 96);
            Check(!cache.IsReady, "returning to the single column invalidates the previous scene geometry");
            await Warm(cache);
            Call(main, "CloseSettings");
            Check(!page.Visible, "leaving settings retains normal navigation while background work is pending");
        }
        finally
        {
            main.WindowState = FormWindowState.Normal;
            Call(main, "CloseSettings");
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, original);
            main.Size = originalSize;
            main.TopMost = false;
            Cursor.Position = cursor;
            Call(main, "ApplyAppearance");
        }
    }
}
