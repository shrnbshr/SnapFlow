using ShareX.HelpersLib;
using ShareX.Roll;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class SettingsViewportRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Private)!.Invoke(target, null);
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private static void Capture(Control control, string path)
    {
        Rectangle bounds = control.RectangleToScreen(control.ClientRectangle);
        Point top = bounds.Location, bottom = new(bounds.Right, bounds.Bottom);
        LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref top);
        LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref bottom);
        nint previous = SetThreadDpiAwarenessContext((nint)(-4));
        try
        {
            using Bitmap bitmap = new(bottom.X - top.X, bottom.Y - top.Y, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(top, Point.Empty, bitmap.Size);
            bitmap.Save(path, ImageFormat.Png);
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }

    internal static async Task RunAsync(MainForm main, bool baseline)
    {
        string output = Path.GetFullPath($"artifacts/settings-wide-layout/{(baseline ? "before" : "after")}/{main.DeviceDpi}");
        Directory.CreateDirectory(output);
        ApplicationPreferences original = Field<ApplicationPreferences>(main, "preferences");
        Size size = main.Size;
        Point location = main.Location, cursor = Cursor.Position;
        FormBorderStyle border = main.FormBorderStyle;
        bool topmost = main.TopMost;
        try
        {
            main.TopMost = true;
            main.ForceActivate();
            Cursor.Position = new Point(0, 0);
            Call(main, "ReleaseCaptureHotkeys");
            ApplicationPreferences working = original.Clone();
            working.AnimationsEnabled = true;
            working.TransparencyEnabled = true;
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, working);
            Call(main, "ApplyAppearance");
            Call(main, "OpenSettings");
            await Task.Delay(450);
            SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
            Panel viewport = Field<Panel>(page, "scroller");
            Control content = Field<Control>(page, "content");
            Control host = Find<Control>(page, "settingsScrollHost");
            Control bar = Find<Control>(page, "settingsScrollBar");
            Control heading = Field<Control>(page, "heading");
            Control footer = Field<Control>(page, "footer");
            void ResizeClient(int width) => main.ClientSize = new Size(width * main.DeviceDpi / 96, 720 * main.DeviceDpi / 96);
            async Task CheckReachable(string name, string context)
            {
                Control last = Find<Control>(page, name);
                viewport.ScrollControlIntoView(last);
                await Task.Delay(100);
                Rectangle visible = page.RectangleToScreen(Rectangle.FromLTRB(0, heading.Bottom, page.Width, footer.Top));
                Check(visible.Contains(last.RectangleToScreen(last.ClientRectangle)),
                    context + ": content remains reachable above the footer and below the heading: " + name);
            }
            foreach (AppearanceMode theme in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                Find<RadioButton>(page, "theme" + theme).Checked = true;
                foreach (string state in new[] { "minimum", "normal", "before-breakpoint", "at-breakpoint", "wide", "ultrawide", "restored-wide", "maximized", "restored", "fullscreen", "restored-fullscreen" })
                {
                    if (state == "minimum") main.Size = main.MinimumSize;
                    if (state == "normal" || state == "restored-wide") ResizeClient(960);
                    if (state == "before-breakpoint") ResizeClient(1239);
                    if (state == "at-breakpoint") ResizeClient(1240);
                    if (state == "wide") ResizeClient(1440);
                    if (state == "ultrawide") ResizeClient(1800);
                    if (state == "maximized") main.WindowState = FormWindowState.Maximized;
                    if (state == "restored") main.WindowState = FormWindowState.Normal;
                    if (state == "fullscreen") { main.FormBorderStyle = FormBorderStyle.None; main.Bounds = Screen.FromControl(main).Bounds; }
                    if (state == "restored-fullscreen") { main.FormBorderStyle = border; main.ClientSize = new Size(960 * main.DeviceDpi / 96, 720 * main.DeviceDpi / 96); }
                    if (main.WindowState == FormWindowState.Normal && state != "fullscreen")
                    {
                        Rectangle area = Screen.FromControl(main).WorkingArea;
                        main.Location = new Point(area.Left + (area.Width - main.Width) / 2, area.Top + (area.Height - main.Height) / 2);
                    }
                    Find<Button>(page, "saveSettingsButton").Focus();
                    viewport.AutoScrollPosition = Point.Empty;
                    // Synthetic AutoScrollPosition moves do not run the normal wheel
                    // paint cycle. Establish a painted reference before screen sampling.
                    viewport.Invalidate(true);
                    viewport.Update();
                    await Task.Delay(350);
                    string context = $"{theme}-{state}";
                    Point track = page.PointToClient(bar.PointToScreen(new Point(bar.Width, 0)));
                    Point contentOrigin = page.PointToClient(content.PointToScreen(Point.Empty));
                    Console.WriteLine($"GEOMETRY {context}: dpi={main.DeviceDpi}; page={page.ClientSize}; host={host.Bounds}; trackRight={track.X}; content={content.Bounds}; contentOrigin={contentOrigin}; heading={heading.Bounds}; hscroll={viewport.HorizontalScroll.Visible}");
                    Capture(main, Path.Combine(output, context + ".png"));
                    int gap = (int)(28 * main.DeviceDpi / 96f);
                    bool wide = page.ClientSize.Width >= (int)(1240 * main.DeviceDpi / 96f);
                    int capped = Math.Min(page.ClientSize.Width - gap * 2, (int)((wide ? 1440 : 760) * main.DeviceDpi / 96f));
                    if (!baseline)
                    {
                        Control[] optionRows = new[] { "transparencyRow", "motionRow", "languageRow", "saveFolderHeader" }
                            .Select(name => Find<Control>(page, name)).ToArray();
                        Console.WriteLine("ROWS " + context + ": " + string.Join("; ", optionRows.Select(row => $"{row.Name}: {row.Bounds}, minimum={row.MinimumSize}, preferred={row.PreferredSize}")));
                        Check(optionRows.Max(row => row.Height) - optionRows.Min(row => row.Height) <= 1,
                            context + ": live resizing preserves the common option header height");
                        Check(!bar.Visible || Math.Abs(page.ClientSize.Width - track.X) <= 1, context + ": visible scrollbar follows the page viewport right edge without an extra outer gutter");
                        Check(heading.Width - heading.Padding.Horizontal == capped && footer.Width - footer.Padding.Horizontal == capped &&
                            heading.Padding.Left == footer.Padding.Left &&
                            Math.Abs(heading.Left + heading.Padding.Left - (page.Width - capped) / 2) <= 1,
                            context + ": heading and footer align with the centered responsive content");
                        int glassRight = bar.Visible ? bar.Left : page.Width;
                        Check(heading.Left == 0 && footer.Left == 0 && heading.Right == glassRight && footer.Right == glassRight &&
                            (!bar.Visible || page.GetChildAtPoint(new Point(bar.Left + bar.Width / 2, heading.Height / 2)) == host),
                            context + ": glass spans both viewport edges without blocking the scrollbar");
                        Check(content.Width == capped - (bar.Visible ? bar.Width : 0) &&
                            Math.Abs(contentOrigin.X - (page.Width - capped) / 2) <= 1,
                            context + ": content keeps its responsive width and alignment");
                        Control general = Find<Control>(page, "settingsGeneralColumn");
                        Control reference = Find<Control>(page, "settingsReferenceColumn");
                        if (wide)
                        {
                            Check(general.Top == reference.Top && Math.Abs(general.Width - reference.Width) <= 1 &&
                                reference.Left - general.Right == gap && reference.Right == content.ClientSize.Width,
                                context + ": the two equal columns share a top edge and the intended gutter");
                            Check(general.Width >= (int)(550 * main.DeviceDpi / 96f),
                                context + ": each column retains room for native fields and shortcut recorders");
                            foreach (string name in new[] { "aboutRepository", "aboutGitHubAccount" })
                            {
                                AboutLinkButton link = Find<AboutLinkButton>(page, name);
                                Check(link.DetailAvailable >= link.DetailWidth,
                                    context + ": project reference remains readable without ellipsis: " + name);
                            }
                            Control links = Find<Control>(page, "aboutLinks");
                            Control about = Find<Control>(page, "aboutSoftware");
                            Check(links.Width + about.Padding.Horizontal <= about.Width,
                                context + ": all three about actions fit the reference column");
                            foreach (ThemeOption option in Find<Control>(page, "appearanceOptions").Controls.OfType<ThemeOption>())
                            {
                                float scale = option.DeviceDpi / 96f;
                                RectangleF face = ButtonElevation.CompactFaceBounds(option.ClientSize, scale);
                                int textLeft = (int)(face.Left + (ButtonSpacing.Horizontal + 54 + 12) * scale);
                                int textWidth = (int)(face.Right - ButtonSpacing.Horizontal * scale) - textLeft;
                                Check(TextRenderer.MeasureText(option.Text, option.Font).Width <= textWidth,
                                    context + ": theme caption fits beside its preview: " + option.Text);
                            }
                            foreach (string name in new[] { "languageSelector", "regionShortcut", "fullShortcut", "scrollShortcut" })
                            {
                                Control field = Find<Control>(page, name);
                                Check(field.Parent!.ClientRectangle.Contains(field.Bounds),
                                    context + ": input stays inside its settings row: " + name);
                            }
                        }
                        else
                        {
                            Check(general.Left == reference.Left && general.Width == content.Width && reference.Width == content.Width &&
                                reference.Top == general.Bottom,
                                context + ": normal windows preserve the original single-column section order");
                        }
                        Check(general.Controls.Contains(Find<Control>(page, "appearanceOptions")) &&
                            general.Controls.Contains(Find<Control>(page, "scrollingOptionsRow")) &&
                            reference.Controls.Contains(Find<Control>(page, "shortcutList")) &&
                            reference.Controls.Contains(Find<Control>(page, "aboutSoftware")),
                            context + ": appearance and capture settings stay together, as do shortcuts and about");
                    }
                    Check(!viewport.HorizontalScroll.Visible && viewport.AutoScrollPosition.X == 0, context + ": no horizontal scroll or clipping");
                    Check(UiMotion.UserEnabled && Field<MotionToggle>(page, "transparency").Checked,
                        context + ": animation and transparency remain enabled");
                    Find<Button>(page, "scrollingOptionsButton").PerformClick();
                    await Task.Delay(300);
                    Check(!viewport.HorizontalScroll.Visible, context + ": expanded settings fit the reading column");
                    viewport.AutoScrollPosition = new Point(0, viewport.DisplayRectangle.Height);
                    await Task.Delay(100);
                    await CheckReachable("cbAutoUpload", context);
                    await CheckReachable("aboutNotice", context);
                    if (state is "at-breakpoint" or "ultrawide" or "maximized")
                    {
                        viewport.AutoScrollPosition = Point.Empty;
                        viewport.Invalidate(true);
                        viewport.Update();
                        Capture(main, Path.Combine(output, context + "-expanded.png"));
                    }
                    Find<Button>(page, "scrollingOptionsButton").PerformClick();
                    await Task.Delay(300);
                }

                // Reflow existing controls while their disclosure is moving. A resize
                // must not recreate native inputs, discard a draft, or toggle a setting.
                ResizeClient(960);
                NumericUpDown delay = Find<NumericUpDown>(page, "nudStartDelay");
                SettingsChoice language = Find<SettingsChoice>(page, "languageSelector");
                ShortcutRecorder shortcut = Find<ShortcutRecorder>(page, "scrollShortcut");
                SettingsDisclosure disclosure = Find<SettingsDisclosure>(page, "scrollingOptionsButton");
                SettingsReveal reveal = Field<SettingsReveal>(page, "scrollingReveal");
                Control editor = Find<Control>(page, "scrollingOptionsEditor");
                delay.Value = 1700;
                language.SelectedIndex = 1;
                shortcut.SetShortcut(Keys.Control | Keys.Alt | Keys.F9);
                nint delayHandle = delay.Handle, shortcutHandle = shortcut.Handle;
                disclosure.PerformClick();
                await Task.Delay(45);
                if (UiMotion.Enabled) Check(reveal.IsAnimating, theme + ": expansion retains its existing animation before reflow");
                ResizeClient(1440);
                await Task.Delay(350);
                Check(disclosure.Expanded && editor.Visible && !reveal.IsAnimating && reveal.Height == editor.Height,
                    theme + ": expansion settles fully after crossing into the wide layout");
                disclosure.PerformClick();
                await Task.Delay(45);
                ResizeClient(960);
                await Task.Delay(350);
                Check(!disclosure.Expanded && !editor.Visible && reveal.Height == 0 && !reveal.IsAnimating,
                    theme + ": collapse settles fully after returning to the normal layout");
                Check(ReferenceEquals(delay, Find<NumericUpDown>(page, "nudStartDelay")) && delay.Handle == delayHandle &&
                    ReferenceEquals(shortcut, Find<ShortcutRecorder>(page, "scrollShortcut")) && shortcut.Handle == shortcutHandle &&
                    delay.Value == 1700 && language.SelectedIndex == 1 && shortcut.Shortcut == (Keys.Control | Keys.Alt | Keys.F9),
                    theme + ": layout changes preserve native controls and unsaved settings");
                Check(Find<RadioButton>(page, "theme" + theme).Checked && UiMotion.UserEnabled &&
                    Field<MotionToggle>(page, "transparency").Checked && !viewport.HorizontalScroll.Visible,
                    theme + ": reflow preserves theme, motion, transparency and vertical-only scrolling");
            }
        }
        finally
        {
            main.WindowState = FormWindowState.Normal;
            main.FormBorderStyle = border;
            Call(main, "CloseSettings");
            typeof(MainForm).GetField("preferences", Private)!.SetValue(main, original);
            main.Size = size;
            main.Location = location;
            Call(main, "ApplyAppearance");
            Call(main, "RegisterCaptureHotkeys");
            main.TopMost = topmost;
            Cursor.Position = cursor;
        }
    }
}
