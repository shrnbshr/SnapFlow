using ShareX.Roll;
using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

internal static class SettingsPolishRegression
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private static Bitmap Render(Control control)
    {
        Bitmap bitmap = new(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        return bitmap;
    }
    private static void Save(MainForm main, string output, string name)
    {
        using Bitmap image = Render(main);
        image.Save(Path.Combine(output, name + "-" + main.DeviceDpi + ".png"), ImageFormat.Png);
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/settings-polish");
        Directory.CreateDirectory(output);
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        Size originalSize = main.Size;
        try
        {
            main.ForceActivate();
            foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                ApplicationPreferences preferences = original.Clone();
                preferences.Appearance = mode;
                preferences.AnimationsEnabled = true;
                foreach (string key in preferences.Shortcuts.Keys.ToArray()) preferences.Shortcuts[key] = Keys.None;
                Set(main, "preferences", preferences);
                Call(main, "ApplyAppearance");
                Find<Button>(main, "settingsButton").PerformClick();
                await Task.Delay(270);
                SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
                Panel viewport = Field<Panel>(page, "scroller");
                TableLayoutPanel content = Field<TableLayoutPanel>(page, "content");
                SettingsGroup group = Find<SettingsGroup>(page, "scrollingOptionsRow");
                SettingsDisclosure header = Find<SettingsDisclosure>(page, "scrollingOptionsButton");
                CapturePalette palette = CapturePalette.For(mode);
                using (Bitmap collapsed = Render(group))
                {
                    int y = group.Padding.Top + group.GetRowHeights()[0];
                    Check(!group.DrawDividers && collapsed.GetPixel(collapsed.Width / 2, y).ToArgb() == palette.Surface.ToArgb(), mode + ": collapsed disclosure has no orphan separator line");
                }
                Save(main, output, "collapsed-" + mode);
                int windows = Application.OpenForms.Count;
                Stopwatch start = Stopwatch.StartNew();
                header.PerformClick();
                start.Stop();
                SettingsReveal? opening = Field<SettingsReveal?>(page, "scrollingReveal");
                if (UiMotion.Enabled)
                {
                    Check(opening?.IsAnimating == true, "expansion starts an interruptible animation");
                    int layouts = 0;
                    LayoutEventHandler count = (_, _) => layouts++;
                    Find<Control>(page, "scrollingOptionsEditor").Layout += count;
                    await Task.Delay(45);
                    float presentation = opening!.CurrentHeight;
                    Check(presentation > 0 && presentation < Find<Control>(page, "scrollingOptionsEditor").Height, "expansion paints an intermediate height");
                    Save(main, output, "opening-frame-" + mode);
                    await Task.Delay(380);
                    Find<Control>(page, "scrollingOptionsEditor").Layout -= count;
                    Check(layouts == 0, "animation frames keep the internal input layout unchanged");
                    double mean = opening.UpdateTicks * 1000d / Stopwatch.Frequency / Math.Max(1, opening.UpdatedFrames);
                    Check(opening.UpdatedFrames > 1 && mean < 16.7, $"{mode}: clipping updates average {mean:F3} ms across {opening.UpdatedFrames} frames; click preparation {start.Elapsed.TotalMilliseconds:F2} ms");
                    Check(!opening.IsAnimating, "completed expansion stops its timer without retaining screenshots");
                }
                viewport.AutoScrollPosition = new Point(0, group.Top);
                Save(main, output, "expanded-" + mode);
                foreach (string name in new[] { "nudStartDelayField", "nudScrollDelayField", "nudScrollAmountField", "cbScrollMethod", "languageSelector" })
                {
                    Control field = Find<Control>(page, name);
                    viewport.ScrollControlIntoView(field);
                    using Bitmap image = Render(field);
                    Check(image.GetPixel(0, 0).ToArgb() == palette.Surface.ToArgb(), mode + ": rounded corners reveal their parent surface: " + name);
                    Check(image.GetPixel(image.Width - (int)(5 * main.DeviceDpi / 96f), image.Height / 2).ToArgb() != Color.White.ToArgb(), "arrow region has no system-white slab: " + name);
                }
                NumericUpDown number = Find<NumericUpDown>(page, "nudStartDelay");
                Console.WriteLine($"METRIC: dpi={number.DeviceDpi}, numeric={number.Bounds}, preferred={number.PreferredHeight}, font={number.Font}, edit={number.Controls.OfType<TextBox>().Single().Bounds}, field={number.Parent!.Bounds}");
                SettingsChoice measuredChoice = Find<SettingsChoice>(page, "cbScrollMethod");
                Console.WriteLine($"METRIC: choice={measuredChoice.Bounds}, itemHeight={measuredChoice.ItemHeight}, font={measuredChoice.Font}");
                Check(number.Left == (int)(10 * number.DeviceDpi / 96f) && number.Top >= 0 && number.Bottom <= number.Parent!.ClientSize.Height,
                    "numeric input is centered at the current DPI without double scaling");
                using (Graphics graphics = number.CreateGraphics())
                {
                    TextBox edit = number.Controls.OfType<TextBox>().Single();
                    int needed = TextRenderer.MeasureText(graphics, number.Maximum.ToString(), number.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
                    Check(edit.Width >= needed + 4 * number.DeviceDpi / 96 && edit.Height >= number.Font.GetHeight(graphics),
                        "the largest allowed number fits without horizontal or vertical text clipping");
                }
                Check(measuredChoice.ItemHeight == (int)(26 * measuredChoice.DeviceDpi / 96f), "choice text height scales with the current DPI");
                decimal value = number.Value;
                number.UpButton();
                Check(number.Value == Math.Min(number.Maximum, value + number.Increment), "numeric field preserves native stepping and range checks");
                number.Text = "1700";
                Check(Find<ScrollingOptionsEditor>(page, "scrollingOptionsEditor").ReadOptions().StartDelay == 1700, "typed numeric text still commits through native validation");
                SettingsNumberField numberField = Find<SettingsNumberField>(page, "nudStartDelayField");
                Call(numberField, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, numberField.Width - 5, 5, 0));
                Call(numberField, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, numberField.Width - 5, 5, 0));
                Check(number.Value == 1800, "rounded stepper hit region increments the same numeric input");

                foreach (string name in new[] { "cbScrollMethod", "languageSelector" })
                {
                    SettingsChoice choice = Find<SettingsChoice>(page, name);
                    viewport.ScrollControlIntoView(choice);
                    Call(choice, "ProcessCmdKey", new Message(), Keys.F4);
                    ToolStripDropDownMenu popup = Field<ToolStripDropDownMenu?>(choice, "menu")!;
                    Check(choice.DroppedDown && popup.Visible && popup.Items.Count == choice.Items.Count, "F4 opens the themed menu with every option: " + name);
                    if (UiMotion.Enabled) Check(choice.MenuAnimating, "choice menu begins a short fade without blocking input");
                    await Task.Delay(190);
                    using (Bitmap menuImage = Render(popup)) menuImage.Save(Path.Combine(output, $"menu-{name}-{mode}-{main.DeviceDpi}.png"), ImageFormat.Png);
                    Check(popup.BackColor.ToArgb() == palette.Surface.ToArgb() && popup.Opacity > .99, "choice popup finishes fully opaque with the correct appearance");
                    int next = choice.SelectedIndex == 0 ? 1 : 0;
                    ((ToolStripMenuItem)popup.Items[next]).PerformClick();
                    Check(choice.SelectedIndex == next && !choice.DroppedDown, "menu selection updates the original value and dismisses the menu");
                    choice.DroppedDown = true;
                    popup = Field<ToolStripDropDownMenu?>(choice, "menu")!;
                    popup.Close(ToolStripDropDownCloseReason.Keyboard);
                    Check(!choice.DroppedDown && !choice.MenuAnimating, "dismissal cancels the menu animation");
                }
                viewport.AutoScrollPosition = new Point(0, group.Top);
                main.Size = main.MinimumSize;
                viewport.ScrollControlIntoView(Find<Control>(page, "cbAutoUpload"));
                Save(main, output, "minimum-" + mode);
                Check(!viewport.HorizontalScroll.Visible, "rounded controls still fit the minimum window without horizontal scrolling");
                main.Size = originalSize;
                viewport.AutoScrollPosition = Point.Empty;
                header.PerformClick();
                if (UiMotion.Enabled)
                {
                    await Task.Delay(45);
                    SettingsReveal closing = Field<SettingsReveal?>(page, "scrollingReveal")!;
                    float position = closing.CurrentHeight;
                    header.PerformClick();
                    SettingsReveal reversed = Field<SettingsReveal?>(page, "scrollingReveal")!;
                    Check(Math.Abs(reversed.CurrentHeight - position) < 2, "reversing starts from the current visible height");
                    await Task.Delay(400);
                }
                UiMotion.UserEnabled = false;
                header.PerformClick();
                Check(!Field<SettingsReveal>(page, "scrollingReveal").IsAnimating && !header.Expanded, "disabled motion collapses immediately without allocating an animation");
                header.PerformClick();
                Check(!Field<SettingsReveal>(page, "scrollingReveal").IsAnimating && header.Expanded, "disabled motion expands immediately");
                Check(Application.OpenForms.Count == windows, "settings remain in the original window");
                Find<Button>(page, "cancelSettingsButton").PerformClick();
                Check(Field<SettingsPage?>(main, "settingsPage") == null, "cancel returns after using rounded controls and menus");
            }
        }
        finally
        {
            Call(main, "CloseSettings");
            Set(main, "preferences", original);
            Call(main, "ApplyAppearance");
            Call(main, "RegisterCaptureHotkeys");
            main.Size = originalSize;
        }
    }
}
