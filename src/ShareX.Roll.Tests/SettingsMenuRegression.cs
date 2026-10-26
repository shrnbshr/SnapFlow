using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class SettingsMenuRegression
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    private static int checks;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Members)!.GetValue(target)!;
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        checks++;
        Console.WriteLine("PASS: " + message);
    }
    private static bool Key(Control control, Keys key) =>
        (bool)typeof(Control).GetMethod("ProcessDialogKey", Members)!.Invoke(control, [key])!;
    private static void Open(SettingsChoice choice, Keys key = Keys.F4) =>
        typeof(SettingsChoice).GetMethod("ProcessCmdKey", Members)!.Invoke(choice, [default(Message), key]);
    private static Rectangle Physical(Control control)
    {
        Point start = control.PointToScreen(Point.Empty), end = control.PointToScreen(new Point(control.ClientSize));
        if (!LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref start) ||
            !LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref end)) throw new InvalidOperationException("DPI mapping failed");
        return Rectangle.FromLTRB(start.X, start.Y, end.X, end.Y);
    }
    private static int Distance(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
    private sealed class Background : Form
    {
        protected override bool ShowWithoutActivation => true;
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/settings-menu-unification/" + main.DeviceDpi);
        Directory.CreateDirectory(output);
        bool motion = UiMotion.UserEnabled;
        Rectangle area = Screen.FromControl(main).WorkingArea;
        main.Hide();
        using Background background = new()
        {
            FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false, Bounds = area
        };
        background.Show();
        try
        {
            UiMotion.UserEnabled = false;
            foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                CapturePalette palette = CapturePalette.For(mode);
                using Form host = new()
                {
                    AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi,
                    ClientSize = new Size(690, 760), TopMost = true, ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual, Text = "Settings menu comparison"
                };
                using SettingsPage page = new(new() { Appearance = mode, AnimationsEnabled = false }, new(), (_, _) => null)
                { Dock = DockStyle.Fill };
                host.Controls.Add(page);
                host.Show();
                host.ClientSize = new Size(690 * host.DeviceDpi / 96, 760 * host.DeviceDpi / 96);
                host.Location = new(area.Left + 8, area.Top + 8);
                await Task.Delay(200);
                Panel viewport = Field<Panel>(page, "scroller");
                Find<SettingsDisclosure>(page, "scrollingOptionsButton").PerformClick();
                MotionToggle transparency = Find<MotionToggle>(page, "transparencyToggle");
                foreach (bool enabled in new[] { true, false })
                {
                    // Read the live draft on every opening, without an appearance refresh or rebuild.
                    transparency.Checked = enabled;
                    foreach (string name in new[] { "languageSelector", "cbScrollMethod" })
                    {
                        SettingsChoice choice = Find<SettingsChoice>(page, name);
                        viewport.ScrollControlIntoView(choice);
                        choice.Focus();
                        int changes = 0;
                        EventHandler changed = (_, _) => changes++;
                        choice.SelectedIndexChanged += changed;
                        Open(choice);
                        ToolStripDropDownMenu menu = Field<ToolStripDropDownMenu>(choice, "menu");
                        string label = $"{mode}-{enabled}-{name}";
                        Check(menu.Visible && choice.DroppedDown && menu.AutoClose && menu.Opacity == 1,
                            label + ": F4 retains native opening, closing and full window opacity");
                        int S(int value) => (int)Math.Round(value * menu.DeviceDpi / 96f);
                        Check(menu.Items.Count == choice.Items.Count && menu.Padding == new Padding(S(6)) && !menu.DropShadowEnabled,
                            label + ": every option uses editor whitespace and its single soft shadow");
                        Check(menu.Items.Cast<ToolStripItem>().All(item => Math.Abs(item.Bounds.Left - (menu.Width - item.Bounds.Right)) <= 1),
                            label + ": highlights have equal left and right whitespace");
                        Check(menu.Items.Cast<ToolStripItem>().All(item =>
                            EditorMenuLayout.TextBounds(item, menu.Font, menu.DeviceDpi, false).Width >=
                            TextRenderer.MeasureText(item.Text, menu.Font, Size.Empty, TextFormatFlags.NoPadding).Width),
                            label + ": option labels fit the shared checkmark and text columns");
                        Rectangle contextBounds = Rectangle.Union(Physical(host), Physical(menu));
                        contextBounds.Inflate(S(24), S(24));
                        using (Bitmap context = new Screenshot { RemoveOutsideScreenArea = false }.CaptureRectangle(contextBounds))
                            context.Save(Path.Combine(output, label + "-page.png"), ImageFormat.Png);

                        // Compare exactly the same rows with the editor's existing default factory path.
                        using Control referenceOwner = new() { Width = Math.Max(choice.Width, choice.DropDownWidth), Font = choice.Font };
                        using ToolStripDropDown reference = EditorOverflow.CreateChoices(referenceOwner, choice, palette, enabled);
                        ToolStripItem selected = menu.Items.Cast<ToolStripItem>().Single(item => item.Selected);
                        reference.Items[menu.Items.IndexOf(selected)].Select();
                        reference.PerformLayout();
                        using Bitmap first = new(menu.Width, menu.Height), second = new(reference.Width, reference.Height);
                        menu.DrawToBitmap(first, new Rectangle(Point.Empty, first.Size));
                        reference.DrawToBitmap(second, new Rectangle(Point.Empty, second.Size));
                        Check(first.Size == second.Size && ImageHelpers.CompareImages(first, second),
                            label + ": all painted pixels match the editor choice menu");

                        menu.AutoClose = false;
                        int inset = S(28);
                        Check(SetWindowPos(menu.Handle, 0, area.Right - menu.Width - inset, area.Top + inset, 0, 0, 0x215)
                            && !host.Bounds.IntersectsWith(menu.Bounds), label + ": desktop sample is clear of the settings window");
                        Rectangle pixels = Physical(menu), frame = pixels;
                        int halo = S(24);
                        frame.Inflate(halo, halo);
                        async Task<Bitmap> Capture(Color color, bool stripes = false)
                        {
                            background.BackColor = color;
                            background.BackgroundImage?.Dispose();
                            background.BackgroundImage = null;
                            if (stripes)
                            {
                                Bitmap pattern = new(8, 8);
                                using Graphics graphics = Graphics.FromImage(pattern);
                                graphics.Clear(Color.Black);
                                graphics.FillRectangle(Brushes.White, 0, 0, 4, 8);
                                background.BackgroundImage = pattern;
                                background.BackgroundImageLayout = ImageLayout.Tile;
                            }
                            background.Refresh();
                            Check(SetWindowPos(background.Handle, host.Handle, 0, 0, 0, 0, 0x213), "controlled background remains behind menus");
                            menu.Refresh();
                            await Task.Delay(250);
                            DwmFlush();
                            return new Screenshot { RemoveOutsideScreenArea = false }.CaptureRectangle(frame);
                        }
                        using Bitmap red = await Capture(Color.Red), blue = await Capture(Color.Blue), stripes = await Capture(Color.Black, true);
                        red.Save(Path.Combine(output, label + "-red.png"), ImageFormat.Png);
                        blue.Save(Path.Combine(output, label + "-blue.png"), ImageFormat.Png);
                        stripes.Save(Path.Combine(output, label + "-blur.png"), ImageFormat.Png);
                        int x = halo + pixels.Width / 2, y = halo + S(3);
                        int response = Distance(red.GetPixel(x, y), blue.GetPixel(x, y));
                        bool active = enabled && WindowBackdrop.IsSupported();
                        Console.WriteLine($"MATERIAL: {label}; active={active}; response={response}");
                        Check(active ? response >= 2 : response <= 3, label + ": only enabled glass reveals the live desktop background");
                        int contrast = 0;
                        for (int offset = -S(32); offset < S(32); offset++)
                            contrast = Math.Max(contrast, Distance(stripes.GetPixel(x + offset, y), stripes.GetPixel(x + offset + 1, y)));
                        Console.WriteLine($"BLUR: {label}; adjacent stripe contrast={contrast}");
                        Check(contrast <= 8, label + ": high frequency background stripes are blurred without blurring foreground ink");
                        if (active)
                        {
                            Check(DwmGetWindowAttribute(menu.Handle, 33, out int corners, sizeof(int)) >= 0 && corners == 2,
                                label + ": native glass uses editor rounded corners");
                        }
                        else Check(menu.Region != null, label + ": solid fallback keeps rounded clipping");
                        int ink = 0;
                        for (int row = halo; row < stripes.Height - halo; row++)
                        for (int column = halo; column < stripes.Width - halo; column++)
                            if (stripes.GetPixel(column, row).ToArgb() == palette.Text.ToArgb()) ink++;
                        Check(ink > 20, label + ": desktop text and checkmark retain opaque theme ink");
                        int shadow = 0;
                        for (int offset = 1; offset <= S(12); offset++)
                            shadow = Math.Max(shadow, Distance(blue.GetPixel(halo + pixels.Width + offset, halo + pixels.Height / 2), Color.Blue));
                        Check(SystemInformation.HighContrast || shadow >= 2, label + ": a soft shadow is visible outside the popup");

                        menu.AutoClose = true;
                        int before = choice.SelectedIndex;
                        Key(menu, Keys.Escape);
                        Check(!choice.DroppedDown && choice.SelectedIndex == before && changes == 0, label + ": Escape cancels without changing the value");
                        Open(choice, Keys.Alt | Keys.Down);
                        Check(menu.IsDisposed, label + ": reopening disposes the prior native material and menu resources");
                        menu = Field<ToolStripDropDownMenu>(choice, "menu");
                        int next = (before + 1) % choice.Items.Count;
                        Key(menu, Keys.Down);
                        Check(menu.Items[next].Selected && choice.SelectedIndex == before,
                            label + ": Down moves native menu focus without committing the setting");
                        Key(menu, Keys.Enter);
                        Check(choice.SelectedIndex == next && changes == 1 && !choice.DroppedDown,
                            label + ": Enter selects once, invokes the original callback and closes");
                        Check(choice.Focused, label + ": selection restores focus to the original combo");
                        if (name == "languageSelector")
                            Check(Field<ApplicationPreferences>(page, "draft").Language == ((LanguageOption)choice.SelectedItem!).Mode,
                                label + ": language selection updates the real settings draft");
                        else Check(Find<ScrollingOptionsEditor>(page, "scrollingOptionsEditor").ReadOptions().ScrollMethod == (ScrollMethod)next,
                            label + ": method selection updates the original capture options");
                        Open(choice, Keys.Space);
                        menu = Field<ToolStripDropDownMenu>(choice, "menu");
                        Check(InputHelpers.SendMouseClick(host.PointToScreen(new Point(host.ClientSize.Width / 2, S(20)))),
                            label + ": physical desktop click reaches the owner window");
                        await Task.Delay(80);
                        Check(!choice.DroppedDown && changes == 1, label + ": clicking outside dismisses without another callback");
                        choice.SelectedIndexChanged -= changed;
                        choice.SelectedIndex = before;
                        choice.CloseMenu();
                        Check(menu.IsDisposed, label + ": closing releases menu, shadow and native observers");
                    }
                }
                host.Close();
            }
        }
        finally
        {
            background.BackgroundImage?.Dispose();
            background.BackgroundImage = null;
            UiMotion.UserEnabled = motion;
            CapturePalette.Current.ApplyLibraryTheme();
        }
        Console.WriteLine($"Settings menus: {checks} checks passed.");
    }
}
