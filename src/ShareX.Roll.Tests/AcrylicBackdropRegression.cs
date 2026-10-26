using Microsoft.Win32;
using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal static class AcrylicBackdropRegression
{
    private const int ExtendedStyle = -20;
    private const long Layered = 0x00080000;
    private const uint RootAncestor = 2;
    private const uint NoSize = 0x0001, NoMove = 0x0002, NoActivate = 0x0010, NoOwnerZOrder = 0x0200;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(nint process, uint flags);

    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Set(object target, string name, object? value) => target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Call(object target, string name, params object[] args) => target.GetType()
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private sealed class BackgroundWindow : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE: changing the test background cannot steal focus.
                return parameters;
            }
        }
    }

    private sealed record ControlState(Control Control, Control? Parent, Rectangle Bounds, Size ClientSize,
        DockStyle Dock, AnchorStyles Anchor, Padding Margin, Padding Padding, bool AutoSize,
        bool Visible, bool Enabled, int TabIndex, bool TabStop, string Text, string Font,
        Color ForeColor, Color? Surface);

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
            foreach (Control descendant in Descendants(child)) yield return descendant;
    }

    private static ControlState[] Snapshot(Control root) => Descendants(root).Select(control => new ControlState(
        control, control.Parent, control.Bounds, control.ClientSize, control.Dock, control.Anchor,
        control.Margin, control.Padding, control.AutoSize, control.Visible, control.Enabled,
        control.TabIndex, control.TabStop, control.Text, control.Font.ToString(), control.ForeColor,
        control is ButtonBase or PreviewCanvas ? control.BackColor : null)).ToArray();

    private static void VerifyUnchanged(Control root, ControlState[] before, string context)
    {
        ControlState[] after = Snapshot(root);
        string[] differences = before.Zip(after).Where(pair => pair.First != pair.Second)
            .Select(pair => pair.First.Control.Name.Length == 0 ? pair.First.Control.GetType().Name : pair.First.Control.Name).ToArray();
        Check(before.Length == after.Length && differences.Length == 0,
            context + ": control hierarchy, bounds, layout, text, typography and control theme are unchanged" +
            (differences.Length == 0 ? "" : " (" + string.Join(", ", differences) + ")"));
    }

    private static void VerifyWindowStyle(MainForm main, string context)
    {
        long style = IntPtr.Size == 8 ? GetWindowLongPtr(main.Handle, ExtendedStyle).ToInt64() : GetWindowLong(main.Handle, ExtendedStyle);
        Check(main.Opacity == 1 && main.TransparencyKey.IsEmpty && (style & Layered) == 0,
            context + ": the main window stays fully opaque at the HWND level without a color key or WS_EX_LAYERED");
    }

    private static void VerifyNativeWindow(MainForm main, Point clientSample, string context)
    {
        VerifyWindowStyle(main, context);
        Point screenSample = main.PointToScreen(clientSample);
        nint hitWindow = WindowFromPoint(screenSample);
        Check(hitWindow != 0 && GetAncestor(hitWindow, RootAncestor) == main.Handle,
            context + ": native hit testing at the glass padding still belongs to SnapFlow");
        nint coordinates = new((screenSample.X & 0xffff) | ((screenSample.Y & 0xffff) << 16));
        long hit = SendMessage(main.Handle, 0x0084, 0, coordinates).ToInt64(); // WM_NCHITTEST
        Check(hit != -1 && hit != 0, context + ": the glass padding does not return HTTRANSPARENT or HTNOWHERE");
    }

    private static byte[] SourceHash(Bitmap source)
    {
        Rectangle rectangle = new(Point.Empty, source.Size);
        BitmapData data = source.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = source.Width * 4;
            byte[] pixels = new byte[rowBytes * source.Height];
            for (int y = 0; y < source.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * rowBytes, rowBytes);
            return SHA256.HashData(pixels);
        }
        finally { source.UnlockBits(data); }
    }

    private static Color Average(Bitmap frame, Point point, int radius)
    {
        if (!new Rectangle(Point.Empty, frame.Size).Contains(point))
            throw new InvalidOperationException($"Desktop sample {point} lies outside the captured client area {frame.Size}");
        long red = 0, green = 0, blue = 0, count = 0;
        for (int y = Math.Max(0, point.Y - radius); y <= Math.Min(frame.Height - 1, point.Y + radius); y++)
            for (int x = Math.Max(0, point.X - radius); x <= Math.Min(frame.Width - 1, point.X + radius); x++)
            {
                Color pixel = frame.GetPixel(x, y);
                red += pixel.R; green += pixel.G; blue += pixel.B; count++;
            }
        return Color.FromArgb((int)(red / count), (int)(green / count), (int)(blue / count));
    }

    private static int Distance(Color first, Color second) => Math.Max(Math.Abs(first.R - second.R),
        Math.Max(Math.Abs(first.G - second.G), Math.Abs(first.B - second.B)));

    private static Point PhysicalClientPoint(Control window, Point clientPoint)
    {
        Point screen = window.PointToScreen(clientPoint);
        if (!LogicalToPhysicalPointForPerMonitorDPI(window.Handle, ref screen))
            throw new InvalidOperationException("Cannot map the test window to physical desktop pixels");
        return screen;
    }

    private static Rectangle PhysicalClientBounds(Control window)
    {
        Point first = PhysicalClientPoint(window, Point.Empty);
        Point last = PhysicalClientPoint(window, new Point(window.ClientSize.Width, window.ClientSize.Height));
        return Rectangle.FromLTRB(first.X, first.Y, last.X, last.Y);
    }

    private static Point FramePoint(Control window, Point clientPoint)
    {
        Point screen = PhysicalClientPoint(window, clientPoint), origin = PhysicalClientPoint(window, Point.Empty);
        return new Point(screen.X - origin.X, screen.Y - origin.Y);
    }

    private static async Task<Bitmap> CaptureAsync(MainForm main, BackgroundWindow background, Color color, string path)
    {
        background.BackColor = color;
        background.Refresh();
        Check(SetWindowPos(background.Handle, main.Handle, 0, 0, 0, 0, NoSize | NoMove | NoActivate | NoOwnerZOrder),
            "the controlled background stays immediately behind SnapFlow");
        main.ForceActivate();
        if (NativeMethods.GetForegroundWindow() != main.Handle)
            InputHelpers.SendMouseClick(main.PointToScreen(new Point(12 * main.DeviceDpi / 96, 12 * main.DeviceDpi / 96)));
        main.Refresh();
        await Task.Delay(450);
        Check(NativeMethods.GetForegroundWindow() == main.Handle, "material sampling owns the foreground after DWM activation settles");
        DwmFlush();
        Rectangle rectangle = PhysicalClientBounds(main);
        Check(PhysicalClientBounds(background).Contains(rectangle),
            "desktop sampling is covered by the dedicated test background");
        Bitmap frame = new Screenshot { RemoveOutsideScreenArea = false }.CaptureRectangle(rectangle);
        frame.Save(path, ImageFormat.Png);
        return frame;
    }

    private static void VerifyBlackPreview(MainForm main, PreviewCanvas preview, Bitmap source, byte[] sourceHash,
        Bitmap frame, string context)
    {
        Check(ReferenceEquals(preview.Image, source) && ReferenceEquals(Field<Bitmap?>(main, "result"), source) &&
            SourceHash(source).SequenceEqual(sourceHash), context + ": the original capture pixels and result bitmap are unchanged");
        RectangleF image = preview.ImageBounds;
        Point previewCenter = new((int)(image.Left + image.Width / 2), (int)(image.Top + image.Height / 2));
        Point center = FramePoint(main, main.PointToClient(preview.PointToScreen(previewCenter)));
        Color pixel = Average(frame, center, Math.Max(1, main.DeviceDpi / 96));
        Check(pixel.R <= 2 && pixel.G <= 2 && pixel.B <= 2,
            context + $": solid black captured pixels remain black on screen ({pixel.R},{pixel.G},{pixel.B})");
        Control surface = Field<Control>(main, "previewFrame");
        int D(int value) => (int)Math.Round(value * main.DeviceDpi / 96d);
        foreach (bool right in new[] { false, true })
        foreach (bool bottom in new[] { false, true })
        {
            Point corner = new(right ? surface.Width - D(2) : D(2), bottom ? surface.Height - D(2) : D(2));
            Point outside = new(right ? surface.Width + D(6) : -D(6), corner.Y);
            Point ToFrame(Point point) => FramePoint(main, main.PointToClient(surface.PointToScreen(point)));
            Color cornerColor = Average(frame, ToFrame(corner), 1);
            Color surrounding = Average(frame, ToFrame(outside), 1);
            Check(Distance(cornerColor, surrounding) <= 3,
                context + $": preview {(bottom ? "bottom" : "top")}-{(right ? "right" : "left")} corner matches the surrounding Acrylic ({Distance(cornerColor, surrounding)})");
        }
    }

    private static bool? ExpectedAcrylic()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) || SystemInformation.HighContrast) return false;
        try
        {
            object? transparency = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1);
            return transparency is not int enabled || enabled != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Console.WriteLine("SKIP: cannot independently read the user's transparency preference: " + ex.Message);
            return null;
        }
    }

    private static void VerifySnapshotPixels()
    {
        foreach (CapturePalette palette in new[] { CapturePalette.Dark, CapturePalette.Light })
        {
            using Panel page = new() { Size = new Size(43, 23) };
            Panel wrapper = new() { Bounds = new Rectangle(7, 3, 30, 16) };
            PreviewSurface surface = new() { Bounds = new Rectangle(3, 2, 17, 9) };
            Panel previewChild = new() { Bounds = new Rectangle(2, 2, 12, 5) };
            PictureBox nestedImage = new() { Bounds = new Rectangle(1, 1, 8, 3) };
            PictureBox imageControl = new() { Bounds = new Rectangle(31, 14, 7, 6) };
            previewChild.Controls.Add(nestedImage);
            surface.Controls.Add(previewChild);
            wrapper.Controls.Add(surface);
            page.Controls.Add(wrapper);
            page.Controls.Add(imageControl);
            using Bitmap image = new(page.Width, page.Height, PixelFormat.Format32bppArgb);
            Point contentPixel = new(40, 1);
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(palette.Background);
                graphics.FillRectangle(Brushes.Black, new Rectangle(contentPixel, new Size(1, 1)));
            }
            byte[] original = SourceHash(image);
            BackdropSnapshot snapshot = BackdropSnapshot.Capture(page, image, palette);
            Rectangle protectedSurface = new(wrapper.Left + surface.Left + previewChild.Left,
                wrapper.Top + surface.Top + previewChild.Top, previewChild.Width, previewChild.Height);
            uint matte = snapshot.Background;
            Check(matte != 0, "snapshot test has a nonzero material matte");
            int incorrect = 0;
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    bool protectedPixel = protectedSurface.Contains(x, y) || imageControl.Bounds.Contains(x, y) || contentPixel == new Point(x, y);
                    if (snapshot.Pixels[y * image.Width + x] != (protectedPixel ? 0u : matte)) incorrect++;
                }
            Check(incorrect == 0, (palette == CapturePalette.Dark ? "dark" : "light") +
                ": preview children and image pixels matching the page background stay protected while frame corners get a material mask");
            Check(SourceHash(image).SequenceEqual(original), "capturing a material mask leaves the source image unchanged");
        }

        uint[] colors = [0xFF806040, 0xFF001020, 0xFF010203, 0xFFABCDEF];
        uint[] mattes = [0x40201808, 0x80402040, 0x00000000, 0xFFABCDEF];
        uint[] expected = [0xBF604838, 0x7F000000, 0xFF010203, 0x00000000];
        Check(Enumerable.Range(0, colors.Length).All(i => BackdropSnapshot.RemoveMatte(colors[i], mattes[i]) == expected[i]),
            "scalar matte removal preserves the expected premultiplied RGBA and clamps subtraction at zero");
        int count = Vector<uint>.Count * 2 + 3; // Includes two vector blocks and a scalar tail.
        uint[] vectorColors = Enumerable.Range(0, count).Select(i => colors[i % colors.Length]).ToArray();
        uint[] vectorMattes = Enumerable.Range(0, count).Select(i => mattes[i % mattes.Length]).ToArray();
        BackdropSnapshot.RemoveMatte(vectorColors.AsSpan(), vectorMattes.AsSpan());
        Check(Enumerable.Range(0, count).All(i => vectorColors[i] == expected[i % expected.Length]),
            "vector matte removal and its scalar tail match independently specified RGBA samples");
    }

    private static void VerifyHandleRecreation(MainForm main, WindowBackdrop backdrop, bool? expectedActive)
    {
        int destroyed = 0, created = 0;
        void Destroyed(object? sender, EventArgs args) => destroyed++;
        void Created(object? sender, EventArgs args) => created++;
        main.HandleDestroyed += Destroyed;
        main.HandleCreated += Created;
        bool previousActive = backdrop.IsActive;
        try
        {
            typeof(Control).GetMethod("RecreateHandle", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
            Check(main.IsHandleCreated && destroyed > 0 && created > 0, "the main form actually destroys and recreates its native handle");
            Check(ReferenceEquals(backdrop, Field<WindowBackdrop>(main, "windowBackdrop")) &&
                backdrop.IsActive == (expectedActive ?? previousActive),
                "handle recreation restores the existing backdrop automatically before any explicit Apply call");
            VerifyWindowStyle(main, "after handle recreation");
        }
        finally
        {
            main.HandleDestroyed -= Destroyed;
            main.HandleCreated -= Created;
        }
    }

    private static async Task VerifyGdiResourcesAsync(MainForm main, WindowBackdrop backdrop, string output)
    {
        using Process process = Process.GetCurrentProcess();
        uint Count() => GetGuiResources(process.Handle, 0); // GR_GDIOBJECTS, not managed allocation counts.
        uint beforeCreate = Count();
        using Panel probe = new()
        {
            Name = "acrylicGdiResizeProbe", Location = new Point(2, 80), Size = new Size(48, 32),
            BackColor = Field<CapturePalette>(main, "appearancePalette").Background
        };
        uint baseline = 0, peak = 0, after = 0;
        try
        {
            main.Controls.Add(probe);
            probe.BringToFront();
            for (int i = 0; i < 6; i++)
            {
                probe.Size = new Size(48 + i % 2, 32 + i % 2);
                probe.Refresh();
            }
            baseline = peak = Count();
            Check(baseline > 0, "GDI object accounting is available for the regression process");
            for (int i = 0; i < 100; i++)
            {
                probe.Size = new Size(48 + i % 2, 32 + i % 2);
                probe.Invalidate(new Rectangle(2, 2, 8, 8));
                probe.Update();
                peak = Math.Max(peak, Count());
                if (i % 20 == 19) await Task.Yield();
            }
            after = Count();
            Check(after <= baseline + 8 && peak <= baseline + 16,
                $"100 local resize/repaint cycles keep GDI objects bounded (active={backdrop.IsActive}, baseline={baseline}, peak={peak}, after={after})");
        }
        finally
        {
            probe.Dispose();
            uint afterDispose = Count();
            File.WriteAllText(Path.Combine(output, "gdi-resources.txt"),
                $"active={backdrop.IsActive}; beforeCreate={beforeCreate}; baseline={baseline}; peak={peak}; after={after}; afterDispose={afterDispose}{Environment.NewLine}");
        }
        Check(Count() <= beforeCreate + 8, "disposing the resize probe releases its native paint surfaces");
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/acrylic-background/" + main.DeviceDpi);
        Directory.CreateDirectory(output);
        Check(!main.InvokeRequired, "acrylic regression runs on the window UI thread");
        Console.WriteLine($"Acrylic desktop regression: {RuntimeInformation.OSDescription}, DPI={main.DeviceDpi}, high contrast={SystemInformation.HighContrast}");
        WindowBackdrop backdrop = Field<WindowBackdrop>(main, "windowBackdrop");
        ApplicationPreferences originalPreferences = Field<ApplicationPreferences>(main, "preferences");
        bool? expectedActive = originalPreferences.TransparencyEnabled ? ExpectedAcrylic() : false;
        if (expectedActive.HasValue)
            Check(backdrop.IsActive == expectedActive.Value,
                "cold startup initializes the material from OS support, high contrast and the user's transparency preference before any test ApplyAppearance call");
        VerifyWindowStyle(main, "cold startup");
        VerifySnapshotPixels();
        PreviewCanvas preview = Field<PreviewCanvas>(main, "preview");
        Bitmap? originalResult = Field<Bitmap?>(main, "result");
        Image? originalImage = preview.Image;
        Rectangle originalBounds = main.Bounds;
        Size originalMinimum = main.MinimumSize;
        FormWindowState originalWindowState = main.WindowState;
        bool originalTopMost = main.TopMost;
        bool[] originalEnabled = new[] { "copyButton", "saveButton", "editButton" }
            .Select(name => Find<Button>(main, name).Enabled).ToArray();
        using Bitmap source = new(256, 256, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(source)) graphics.Clear(Color.Black);
        byte[] sourceHash = SourceHash(source);
        using BackgroundWindow background = new()
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            TopMost = true, BackColor = Color.Red, Name = "acrylicControlledBackground"
        };
        try
        {
            Call(main, "CloseSettings");
            Call(main, "EndPageTransition");
            Call(main, "ReleaseCaptureHotkeys");
            main.WindowState = FormWindowState.Normal;
            Rectangle work = Screen.FromControl(main).WorkingArea;
            int D(int logical) => (int)Math.Round(logical * main.DeviceDpi / 96d);
            Size available = new(Math.Max(1, work.Width - D(32)), Math.Max(1, work.Height - D(32)));
            double fit = Math.Min(1, Math.Min(available.Width / (double)D(980), available.Height / (double)D(760)));
            main.MinimumSize = new Size(Math.Min(originalMinimum.Width, available.Width), Math.Min(originalMinimum.Height, available.Height));
            main.Size = new Size((int)(D(980) * fit), (int)(D(760) * fit));
            main.Location = new Point(work.Left + (work.Width - main.Width) / 2, work.Top + (work.Height - main.Height) / 2);
            Check(work.Contains(main.Bounds), "the regression window fits the current monitor working area at this DPI");
            background.Bounds = work;
            background.Show();
            main.TopMost = true;
            main.ForceActivate();
            Set(main, "result", null); // Preserve any pre-existing capture: SetPreview disposes its previous result.
            preview.Image = null;
            Call(main, "SetPreview", source);
            await Task.Delay(400);
            VerifyHandleRecreation(main, backdrop, expectedActive);
            main.ForceActivate();
            await Task.Delay(250); // Let existing controls rebuild their paint caches before counting resources.
            await VerifyGdiResourcesAsync(main, backdrop, output);
            List<string> results = new();
            foreach (AppearanceMode mode in new[] { AppearanceMode.Dark, AppearanceMode.Light })
            {
                ApplicationPreferences preferences = originalPreferences.Clone();
                preferences.TransparencyEnabled = true;
                preferences.Appearance = mode;
                foreach (string kind in preferences.Shortcuts.Keys.ToArray()) preferences.Shortcuts[kind] = Keys.None;
                Set(main, "preferences", preferences);
                Call(main, "ApplyAppearance");
                await Task.Delay(320);
                CapturePalette palette = CapturePalette.For(mode);
                Control workspace = Field<Control>(main, "workspace");
                ControlState[] initial = Snapshot(workspace);
                Point glassSample = new(D(6), main.ClientSize.Height / 2);
                int radius = Math.Max(1, D(1));
                foreach (bool enabled in new[] { false, true })
                {
                    backdrop.Apply(palette, enabled);
                    await Task.Delay(100);
                    bool active = backdrop.IsActive;
                    Check(enabled || !active, mode + ": explicitly disabling the material enters the opaque fallback");
                    VerifyUnchanged(workspace, initial, mode + (enabled ? " acrylic" : " fallback"));
                    Check(Field<CapturePalette>(main, "appearancePalette") == palette && preview.Palette == palette,
                        mode + ": material changes preserve the selected application palette");
                    string prefix = mode + (enabled ? "-enabled" : "-fallback");
                    using Bitmap red = await CaptureAsync(main, background, Color.Red, Path.Combine(output, prefix + "-red.png"));
                    VerifyNativeWindow(main, glassSample, prefix);
                    VerifyBlackPreview(main, preview, source, sourceHash, red, prefix + " over red");
                    using Bitmap blue = await CaptureAsync(main, background, Color.Blue, Path.Combine(output, prefix + "-blue.png"));
                    VerifyNativeWindow(main, glassSample, prefix);
                    VerifyBlackPreview(main, preview, source, sourceHash, blue, prefix + " over blue");
                    Point pixelSample = FramePoint(main, glassSample);
                    Color redPixel = Average(red, pixelSample, radius), bluePixel = Average(blue, pixelSample, radius);
                    int response = (redPixel.R - redPixel.B) - (bluePixel.R - bluePixel.B);
                    string result = $"{prefix}: active={active}, red={redPixel}, blue={bluePixel}, response={response}";
                    Console.WriteLine(result);
                    results.Add(result);
                    File.WriteAllLines(Path.Combine(output, "composition-results.txt"), results);
                    if (active)
                        Check(response >= 8, prefix + ": actual composed glass responds to the controlled red/blue window behind it");
                    else
                    {
                        Check(Distance(redPixel, bluePixel) <= 3, prefix + ": opaque fallback does not leak the background window");
                        if (enabled) Console.WriteLine("SKIP: native acrylic composition is unavailable; opaque fallback was verified for " + mode);
                    }
                }
                backdrop.Apply(palette);
                Find<Button>(main, "settingsButton").PerformClick();
                await Task.Delay(400);
                SettingsPage page = Find<SettingsPage>(main, "SettingsPage");
                AppearanceMode previewMode = mode == AppearanceMode.Dark ? AppearanceMode.Light : AppearanceMode.Dark;
                Find<RadioButton>(page, "theme" + previewMode).Checked = true;
                await Task.Delay(320);
                Check(Field<CapturePalette>(main, "appearancePalette") == CapturePalette.For(previewMode),
                    mode + ": selecting the opposite settings theme still previews its palette");
                using (Bitmap settings = await CaptureAsync(main, background, Color.Blue, Path.Combine(output, mode + "-settings.png")))
                    VerifyNativeWindow(main, glassSample, mode + " settings");
                Find<Button>(page, "cancelSettingsButton").PerformClick();
                await Task.Delay(450);
                Check(Field<SettingsPage?>(main, "settingsPage") == null && workspace.Visible &&
                    Field<ApplicationPreferences>(main, "preferences").Appearance == mode &&
                    Field<CapturePalette>(main, "appearancePalette") == palette,
                    mode + ": cancelling settings restores the workspace and its saved theme");
                using Bitmap returned = await CaptureAsync(main, background, Color.Red, Path.Combine(output, mode + "-returned.png"));
                VerifyNativeWindow(main, glassSample, mode + " returned");
                VerifyBlackPreview(main, preview, source, sourceHash, returned, mode + " returned");
            }
            source.Save(Path.Combine(output, "unchanged-source-black.png"), ImageFormat.Png);
            File.WriteAllLines(Path.Combine(output, "composition-results.txt"), results);
        }
        finally
        {
            background.Hide();
            Call(main, "CloseSettings");
            Call(main, "EndPageTransition");
            Set(main, "preferences", originalPreferences);
            Set(main, "result", originalResult);
            preview.Image = originalImage;
            preview.SynchronizeImage();
            string[] buttons = ["copyButton", "saveButton", "editButton"];
            for (int i = 0; i < buttons.Length; i++) Find<Button>(main, buttons[i]).Enabled = originalEnabled[i];
            Call(main, "ApplyAppearance");
            Call(main, "RefreshPreviewToolbar");
            Call(main, "RegisterCaptureHotkeys");
            main.MinimumSize = originalMinimum;
            main.Bounds = originalBounds;
            main.WindowState = originalWindowState;
            main.TopMost = originalTopMost;
        }
    }
}
