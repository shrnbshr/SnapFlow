using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.IO;

internal static class WorkspacePreviewRegression
{
    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private static async Task WaitAsync(Func<bool> ready, string message, int seconds = 15)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!ready() && watch.Elapsed.TotalSeconds < seconds) await Task.Delay(50);
        Check(ready(), message);
    }

    // Drive the released selection overlay and capture manager against our own local fixture.
    private static async Task SelectAsync(MainForm main, Rectangle area, bool cancel = false, string kind = "scroll", bool click = false)
    {
        Task input = Task.Run(async () =>
        {
            Stopwatch watch = Stopwatch.StartNew();
            bool Selecting() => (bool)main.Invoke((Func<bool>)(() => Application.OpenForms.OfType<RegionCaptureForm>().Any()));
            while (!Selecting() && watch.Elapsed.TotalSeconds < 8) await Task.Delay(50);
            if (!Selecting()) throw new TimeoutException("Selection overlay did not open");
            await Task.Delay(200);
            if (cancel) InputHelpers.SendKeyPress(VirtualKeyCode.ESCAPE);
            else if (click) InputHelpers.SendMouseClick(area.Location);
            else
            {
                Point start = area.Location;
                Point end = new(area.Right, area.Bottom);
                InputHelpers.SendMouseMove(start);
                InputHelpers.SendMouseDown();
                await Task.Delay(120);
                InputHelpers.SendMouseMove(end);
                await Task.Delay(120);
                InputHelpers.SendMouseUp();
            }
        });
        await main.CaptureAsync(kind);
        await input;
    }

    internal static async Task RunMergedAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/merged-capture");
        Directory.CreateDirectory(output);
        Check(ApplicationPreferences.Actions.Length == 3 && !main.Controls.Find("windowButton", true).Any(), "main has one merged selector, full screen and scrolling");
        Button button = (Button)main.Controls.Find("regionButton", true).Single();
        Check(button.Text == "区域/窗口截图", "merged capture has an explicit label");
        main.Controls.Find("settingsButton", true).OfType<Button>().Single().PerformClick();
        Check(!main.Controls.Find("windowShortcut", true).Any() && main.Controls.Find("regionShortcut", true).Length == 1,
            "settings has a single shortcut for region and window selection");
        main.Controls.Find("cancelSettingsButton", true).OfType<Button>().Single().PerformClick();
        await Task.Delay(250);

        string legacyPath = Path.Combine(output, "legacy-preferences.json");
        ApplicationPreferences legacy = new();
        legacy.Shortcuts["region"] = Keys.Control | Keys.Alt | Keys.F10;
        legacy.Shortcuts["window"] = Keys.Control | Keys.Alt | Keys.F11;
        legacy.Save(legacyPath);
        ApplicationPreferences loaded = ApplicationPreferences.Load(legacyPath);
        Check(loaded.Shortcuts["region"] == legacy.Shortcuts["region"] && !loaded.Shortcuts.ContainsKey("window"), "legacy region binding takes priority and duplicate action is removed");
        legacy.Shortcuts["region"] = Keys.None;
        legacy.Save(legacyPath);
        loaded = ApplicationPreferences.Load(legacyPath);
        Check(loaded.Shortcuts["region"] == legacy.Shortcuts["window"], "legacy window-only shortcut becomes the merged shortcut");
        legacy.Shortcuts.Remove("region");
        legacy.Save(legacyPath);
        Check(ApplicationPreferences.Load(legacyPath).Shortcuts["region"] == legacy.Shortcuts["window"], "missing legacy region binding also inherits window binding");
        legacy.Shortcuts["region"] = legacy.Shortcuts["window"] = Keys.None;
        legacy.Save(legacyPath);
        Check(ApplicationPreferences.Load(legacyPath).Shortcuts["region"] == Keys.None, "disabled region and window shortcuts stay disabled");
        File.WriteAllText(legacyPath, "{\"Shortcuts\":{}}");
        Check(ApplicationPreferences.Load(legacyPath).Shortcuts["region"] == (Keys.Control | Keys.Shift | Keys.A), "new and partial settings use the merged default shortcut");

        using Form target = new()
        {
            Text = "SnapFlow merged selection fixture", StartPosition = FormStartPosition.Manual,
            Location = new Point(100, 100), ClientSize = new Size(700, 460), TopMost = true,
            BackColor = Color.CornflowerBlue
        };
        target.Show(); target.Activate();
        await Task.Delay(200);
        Rectangle area = new(target.PointToScreen(new Point(25, 50)), new Size(240, 160));
        await SelectAsync(main, area, kind: "region");
        Bitmap cropped = Field<Bitmap>(main, "result");
        Check(cropped.Size == new Size(area.Width + 1, area.Height + 1), "merged selector drag crops the exact region with inclusive endpoints");
        Check(main.Visible && ReferenceEquals(cropped, Field<PictureBox>(main, "preview").Image), "drag result is displayed on the right");

        Point title = new(target.Left + 150, target.Top + SystemInformation.CaptionHeight / 2 + 4);
        target.Activate();
        await SelectAsync(main, new Rectangle(title, Size.Empty), kind: "region", click: true);
        Bitmap window = Field<Bitmap>(main, "result");
        Check(window.Width >= target.ClientSize.Width && window.Height >= target.ClientSize.Height, "same merged selector clicks a complete window");
        Check(main.Visible && ReferenceEquals(window, Field<PictureBox>(main, "preview").Image), "window result is displayed on the right");
        Check(Field<Button>(main, "copy").Enabled && Field<Button>(main, "save").Enabled, "both results use the main copy and save actions");
        await SelectAsync(main, area, cancel: true, kind: "region");
        Check(main.Visible && ReferenceEquals(window, Field<Bitmap>(main, "result")), "cancel keeps the previous preview");
        target.Hide();
        using Bitmap rendered = new Screenshot().CaptureWindow(main.Handle);
        rendered.Save(Path.Combine(output, "main-" + main.DeviceDpi + ".png"), ImageFormat.Png);
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/workspace-preview");
        Directory.CreateDirectory(output);
        await main.CaptureAsync("full");
        PictureBox preview = Field<PictureBox>(main, "preview");
        Bitmap seed = Field<Bitmap>(main, "result");
        Check(ReferenceEquals(seed, preview.Image), "ordinary capture is displayed in the main preview");

        using Form target = new()
        {
            Text = "SnapFlow workspace preview fixture", StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(100, 100, 760, 540), TopMost = true
        };
        Panel panel = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
        target.Controls.Add(panel);
        for (int i = 0; i < 60; i++)
            panel.Controls.Add(new Label
            {
                Text = $"{i:D3}   SnapFlow scrolling preview / 截图预览 {i * 7919}",
                Bounds = new Rectangle(0, i * 42, 680, 42), TextAlign = ContentAlignment.MiddleLeft,
                BackColor = i % 2 == 0 ? Color.AliceBlue : Color.White
            });
        target.Show();
        await Task.Delay(200);
        Point origin = panel.PointToScreen(new Point(20, 20));
        Rectangle area = new(origin, new Size(610, 380));
        try
        {
            await SelectAsync(main, area, cancel: true);
            await WaitAsync(() => main.Visible && !Application.OpenForms.OfType<ScrollingCaptureForm>().Any(), "cancel returns directly to the main window");
            Check(ReferenceEquals(seed, preview.Image), "cancel preserves the previous image");

            target.Activate();
            await SelectAsync(main, area);
            await WaitAsync(() => main.Visible && !Application.OpenForms.OfType<ScrollingCaptureForm>().Any(), "completed scrolling capture returns without a separate preview", 35);
            Bitmap image = Field<Bitmap>(main, "result");
            Check(!ReferenceEquals(seed, image) && ReferenceEquals(image, preview.Image), "main preview owns the new scrolling image");
            Check(image.Height > area.Height * 2, "real scrolling produces a long image");
            Check(Field<Label>(main, "previewHint").Text.Contains(image.Height.ToString()), "dimensions update when replacing an existing result");
            Check(Field<Button>(main, "copy").Enabled && Field<Button>(main, "save").Enabled, "main copy and save actions are enabled");
            Check(!Application.OpenForms.OfType<ScrollingCaptureForm>().Any(), "capture form and its manager are disposed after transfer");
            using (Bitmap expected = (Bitmap)image.Clone())
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                Check(ImageHelpers.CompareImages(expected, image), "original pixels survive manager disposal and garbage collection");
                string path = Path.Combine(output, "scroll-result.png");
                Check(ImageHelpers.SaveImage(image, path), "full scrolling result can be saved");
                using Bitmap loaded = new(path);
                Check(ImageHelpers.CompareImages(expected, loaded), "saved result retains every full-resolution pixel");
                IDataObject? clipboard = Clipboard.GetDataObject();
                try
                {
                    Field<Button>(main, "copy").PerformClick();
                    Check(Clipboard.ContainsImage(), "main copy button writes an image");
                    using Image copied = Clipboard.GetImage()!;
                    Check(copied.Size == image.Size, "clipboard contains the full image dimensions");
                }
                finally { if (clipboard != null) Clipboard.SetDataObject(clipboard, true); else Clipboard.Clear(); }
            }
            target.Hide();
            main.ForceActivate();
            await Task.Delay(250);
            using (Bitmap rendered = new Screenshot().CaptureWindow(main.Handle)) rendered.Save(Path.Combine(output, "main-preview.png"), ImageFormat.Png);

            target.Show(); target.Activate();
            panel.AutoScrollPosition = Point.Empty;
            await SelectAsync(main, area);
            await WaitAsync(() => Application.OpenForms.OfType<ScrollingCaptureForm>().Any(), "another scrolling capture starts after completion");
            await Task.Delay(450);
            await main.CaptureAsync("scroll");
            await WaitAsync(() => main.Visible && !Application.OpenForms.OfType<ScrollingCaptureForm>().Any(), "same capture shortcut stops and returns to the main preview");
            Check(Field<Bitmap>(main, "result").Height >= area.Height, "stopping retains the available captured image");

            panel.Controls.Clear();
            panel.AutoScrollMinSize = Size.Empty;
            panel.AutoScrollPosition = Point.Empty;
            await SelectAsync(main, area);
            ScrollingCaptureForm staticForm = Field<ScrollingCaptureForm>(main, "scrollForm");
            Rectangle staticArea = Field<Rectangle>(Field<object>(staticForm, "manager"), "selectedRectangle");
            await WaitAsync(() => main.Visible && !Application.OpenForms.OfType<ScrollingCaptureForm>().Any(), "static capture also returns to the main preview");
            Check(Field<Label>(main, "status").Text.Contains("未检测到滚动"), "main window preserves the no-scroll warning");
            Check(Field<Bitmap>(main, "result").Size == staticArea.Size, "static target produces exactly the selected single screen");

            target.Hide();
            main.Controls.Find("settingsButton", true).OfType<Button>().Single().PerformClick();
            await Task.Delay(250);
            main.Controls.Find("cancelSettingsButton", true).OfType<Button>().Single().PerformClick();
            await Task.Delay(250);
            Check(ReferenceEquals(Field<Bitmap>(main, "result"), preview.Image), "settings round trip preserves the preview");
            await main.CaptureAsync("full");
            Check(Field<Bitmap>(main, "result").Size == SystemInformation.VirtualScreen.Size, "ordinary capture still replaces a scrolling result");
        }
        finally { target.Close(); }
    }
}
