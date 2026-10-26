using ShareX.Roll;
using ShareX.Roll.Localization;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class PreviewZoomRegression
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void Check(bool condition, string text)
    {
        if (!condition) throw new InvalidOperationException(text);
        Console.WriteLine("PASS: " + text);
    }
    private static async Task Settle(PreviewCanvas preview)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (preview.IsViewportAnimating && clock.ElapsedMilliseconds < 2000) await Task.Delay(20);
        Check(!preview.IsViewportAnimating, "viewport settles and stops its frame timer");
    }
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static async Task WaitForFadeAsync(PreviewZoomFade fade)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (fade.IsAnimating && watch.ElapsedMilliseconds < 1500) await Task.Delay(20);
    }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);

    private static Bitmap Document()
    {
        Bitmap image = new(1100, 8800, PixelFormat.Format32bppArgb);
        image.SetResolution(96, 96);
        using Graphics g = Graphics.FromImage(image);
        g.Clear(Color.White);
        using Font title = new("Microsoft YaHei UI", 34, FontStyle.Bold);
        using Font body = new("Microsoft YaHei UI", 23);
        for (int i = 0; i < 22; i++)
        {
            int y = i * 400;
            g.FillRectangle(i % 2 == 0 ? Brushes.AliceBlue : Brushes.White, 0, y, 1100, 400);
            g.DrawString($"{i + 1:00}   SnapFlow · 长图预览", title, Brushes.MidnightBlue, 60, y + 52);
            g.DrawString("向下滚动阅读，放大查看图片细节。", body, Brushes.DimGray, 60, y + 146);
            g.FillRectangle(Brushes.CornflowerBlue, 60, y + 230, 980, 6);
        }
        g.FillRectangle(Brushes.Coral, 0, 8740, 1100, 60);
        return image;
    }

    private static async Task VerifyHoverAsync(MainForm main, Image source, Size size, string output)
    {
        // Keep this native control outside the client area so live user pointer movement cannot
        // interfere with deterministic enter/leave and opacity checks.
        using PreviewCanvas preview = new() { Location = new Point(-10000, -10000), Size = size, Image = source, SizeMode = PictureBoxSizeMode.Zoom };
        main.Controls.Add(preview);
        preview.SynchronizeImage();
        Control toolbar = preview.Controls.Find("previewZoomBar", true).Single();
        PreviewZoomFade fade = (PreviewZoomFade)preview.Controls.Find("previewZoomFade", true).Single();
        Point inside = preview.PointToScreen(new Point(preview.Width / 2, preview.Height / 2));
        Point outside = preview.PointToScreen(new Point(-20, -20));
        bool originalMotion = UiMotion.UserEnabled;
        UiMotion.UserEnabled = true;
        try
        {
            Check(!toolbar.Visible && fade.Opacity == 0, "captured image keeps floating controls hidden until hover");
            preview.UpdateZoomBarPointer(inside);
            await Task.Delay(45);
            if (UiMotion.Enabled)
            {
                Check(fade.IsAnimating && fade.Opacity > 0 && fade.Opacity < 1, "hover fades in the entire floating toolbar");
                UiTween opacity = Field<UiTween>(fade, "opacity");
                float current = opacity.Value, velocity = opacity.Velocity;
                Check(velocity > 0, "toolbar arrival starts with a continuous spring velocity");
                preview.UpdateZoomBarPointer(outside);
                Check(opacity.Value == current && opacity.Velocity == velocity, "reversing fade-out preserves both opacity and velocity");
                preview.UpdateZoomBarPointer(inside);
                Check(opacity.Value == current && opacity.Velocity == velocity, "immediate reentry preserves the same motion");
                using Bitmap low = new(fade.Width, fade.Height), high = new(fade.Width, fade.Height);
                opacity.SnapTo(.25f); fade.DrawToBitmap(low, fade.ClientRectangle);
                opacity.SnapTo(.75f); fade.DrawToBitmap(high, fade.ClientRectangle);
                int different = 0;
                for (int pixelY = 2; pixelY < fade.Height - 2; pixelY += 3)
                for (int pixelX = 2; pixelX < fade.Width - 2; pixelX += 3)
                    if (low.GetPixel(pixelX, pixelY).ToArgb() != high.GetPixel(pixelX, pixelY).ToArgb()) different++;
                Check(different > 10, "intermediate opacity changes the rendered toolbar pixels");
                high.Save(Path.Combine(output, $"hover-fade-{main.DeviceDpi}.png"), ImageFormat.Png);
                Stopwatch painting = Stopwatch.StartNew();
                long allocation = GC.GetAllocatedBytesForCurrentThread();
                for (int sample = 0; sample < 12; sample++) fade.DrawToBitmap(high, fade.ClientRectangle);
                painting.Stop();
                Console.WriteLine($"PAINT: {main.DeviceDpi} DPI, {fade.Width}x{fade.Height}, 12 native DrawToBitmap samples, {painting.Elapsed.TotalMilliseconds / 12:F3} ms/sample, {(GC.GetAllocatedBytesForCurrentThread() - allocation) / 12} managed bytes/sample (not an on-screen frame-rate measurement)");
                preview.UpdateZoomBarPointer(outside);
                float intermediate = fade.Opacity;
                preview.UpdateZoomBarPointer(inside);
                Check(fade.Opacity == intermediate, "rapid leave and reentry reverses from the current opacity");
            }
            await WaitForFadeAsync(fade);
            Check(toolbar.Visible && fade.Opacity == 1 && !fade.IsAnimating && !fade.Visible,
                "completed fade reveals live controls and stops scheduling");
            Control plus = preview.Controls.Find("previewZoomIn", true).Single();
            preview.UpdateZoomBarPointer(plus.PointToScreen(new Point(plus.Width / 2, plus.Height / 2)));
            Call(plus, "OnMouseEnter", EventArgs.Empty);
            await Task.Delay(60);
            Check(toolbar.Visible && fade.Opacity == 1 && !fade.IsAnimating, "moving onto a zoom button does not hide the toolbar");
            await VerifyZoomButtonAsync((Button)plus, output, main.DeviceDpi);
            preview.UpdateZoomBarPointer(outside);
            await Task.Delay(100);
            if (UiMotion.Enabled) Check(toolbar.Visible && fade.Opacity == 1 && !fade.IsAnimating, "toolbar remains visible briefly after pointer leaves");
            preview.UpdateZoomBarPointer(inside);
            await Task.Delay(260);
            Check(toolbar.Visible && fade.Opacity == 1, "returning during the hold cancels the pending hide");
            preview.UpdateZoomBarPointer(outside);
            if (UiMotion.Enabled)
            {
                Stopwatch waiting = Stopwatch.StartNew();
                while (fade.Opacity == 1 && waiting.ElapsedMilliseconds < 1000) await Task.Delay(15);
                Check(fade.Opacity < 1, "leaving starts fade-out after the hold");
            }
            await WaitForFadeAsync(fade);
            Check(!toolbar.Visible && fade.Opacity == 0 && !fade.IsAnimating && Field<Bitmap?>(fade, "snapshot") == null,
                "completed fade hides controls, releases its small snapshot and stops scheduling");
            UiMotion.UserEnabled = false;
            preview.UpdateZoomBarPointer(inside);
            Check(toolbar.Visible && fade.Opacity == 1 && !fade.IsAnimating, "reduced motion shows controls immediately");
            preview.UpdateZoomBarPointer(outside);
            Check(!toolbar.Visible && fade.Opacity == 0 && !fade.IsAnimating, "reduced motion hides controls immediately");
        }
        finally { preview.Image = null; UiMotion.UserEnabled = originalMotion; }
    }

    private static void VerifyTransparency(MainForm main, string output)
    {
        bool originalMotion = UiMotion.UserEnabled;
        UiMotion.UserEnabled = false;
        using Bitmap image = new(640, 360);
        using PreviewCanvas preview = new() { Location = new Point(-10000, -10000), Size = new Size(640, 360), Image = image };
        main.Controls.Add(preview);
        try
        {
            preview.SynchronizeImage();
            preview.SetZoomBarHovered(true);
            PreviewZoomBar bar = (PreviewZoomBar)preview.Controls.Find("previewZoomBar", true).Single();
            Control button = bar.Controls.Find("previewZoomIn", true).Single();
            float scale = bar.DeviceDpi / 96f;
            Point sample = new(button.Left + button.Width / 2, button.Top + (int)(3 * scale));
            foreach (CapturePalette palette in new[] { CapturePalette.Light, CapturePalette.Dark })
            {
                preview.Palette = palette;
                preview.ApplyViewportAppearance();
                preview.ZoomTransparencyEnabled = true;
                using Bitmap red = new(bar.Width, bar.Height), blue = new(bar.Width, bar.Height);
                using (Graphics g = Graphics.FromImage(image)) g.Clear(Color.Red);
                bar.DrawToBitmap(red, bar.ClientRectangle);
                using (Graphics g = Graphics.FromImage(image)) g.Clear(Color.Blue);
                preview.Invalidate();
                bar.DrawToBitmap(blue, bar.ClientRectangle);
                string theme = palette == CapturePalette.Light ? "Light" : "Dark";
                Color a = red.GetPixel(sample.X, sample.Y), b = blue.GetPixel(sample.X, sample.Y);
                Check(a.R > b.R + 25 && b.B > a.B + 25, theme + " preview pixels show through native zoom buttons");
                using Bitmap material = new(bar.Width, bar.Height);
                using (Graphics g = Graphics.FromImage(material)) bar.PaintSurface(g, Point.Empty);
                Color surface = material.GetPixel(sample.X, sample.Y);
                Check(Math.Abs(surface.R - b.R) <= 1 && Math.Abs(surface.B - b.B) <= 1,
                    theme + " toolbar and button backgrounds form a continuous material");
                preview.ZoomTransparencyEnabled = false;
                using Bitmap opaque = new(bar.Width, bar.Height);
                bar.DrawToBitmap(opaque, bar.ClientRectangle);
                Check(opaque.GetPixel(sample.X, sample.Y).ToArgb() == palette.Surface.ToArgb(),
                    theme + " transparency setting restores an opaque zoom toolbar");
                preview.ZoomTransparencyEnabled = true;
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.Clear(Color.White);
                    int stripe = Math.Max(1, (int)(2 * scale));
                    for (int x = 0; x < image.Width; x += 2 * stripe)
                        g.FillRectangle(Brushes.Black, x, 0, stripe, image.Height);
                }
                preview.Invalidate();
                using Bitmap frosted = new(bar.Width, bar.Height);
                bar.DrawToBitmap(frosted, bar.ClientRectangle);
                int darkest = 255, brightest = 0;
                for (int x = button.Left + (int)(8 * scale); x < button.Right - (int)(8 * scale); x++)
                {
                    int redChannel = frosted.GetPixel(x, sample.Y).R;
                    darkest = Math.Min(darkest, redChannel); brightest = Math.Max(brightest, redChannel);
                }
                Check(brightest - darkest < 12, theme + " frosted background suppresses sharp image detail behind the controls");
                {
                    bool light = palette == CapturePalette.Light;
                    using (Graphics g = Graphics.FromImage(image)) g.Clear(light ? Color.White : palette.Canvas);
                    preview.Invalidate();
                    using Bitmap solid = new(preview.Width, preview.Height);
                    preview.DrawToBitmap(solid, preview.ClientRectangle);
                    solid.Save(Path.Combine(output, $"{(light ? "white" : "dark")}-background-{main.DeviceDpi}.png"), ImageFormat.Png);
                }
                // Include patterned content in a complete preview for visual inspection.
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.Clear(palette.Canvas);
                    for (int x = 0; x < image.Width; x += 40)
                        g.FillRectangle(x % 80 == 0 ? Brushes.CornflowerBlue : Brushes.Coral, x, 0, 40, image.Height);
                    using Font detail = new("Microsoft YaHei UI", 15 * scale);
                    g.DrawString("背景文字 · Preview image detail", detail, Brushes.Black, 0, bar.Top + 12 * scale);
                }
                preview.Invalidate();
                using Bitmap shot = new(preview.Width, preview.Height);
                preview.DrawToBitmap(shot, preview.ClientRectangle);
                shot.Save(Path.Combine(output, $"transparency-{theme}-{main.DeviceDpi}.png"), ImageFormat.Png);
            }
        }
        finally { preview.Image = null; UiMotion.UserEnabled = originalMotion; }
    }

    private static async Task VerifyZoomButtonAsync(Button button, string output, int dpi)
    {
        object table = typeof(CaptureButtonPainter).GetField("states", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object?[] lookup = [button, null];
        table.GetType().GetMethod("TryGetValue")!.Invoke(table, lookup);
        UiTween press = Field<UiTween>(lookup[1]!, "Press");
        UiTween hover = Field<UiTween>(lookup[1]!, "Hover");
        Rectangle bounds = button.Bounds;
        using Bitmap idle = new(button.Width, button.Height), down = new(button.Width, button.Height);
        press.SnapTo(0); hover.SnapTo(0); button.DrawToBitmap(idle, button.ClientRectangle);
        Call(button, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, button.Width / 2, button.Height / 2, 0));
        button.DrawToBitmap(down, button.ClientRectangle);
        Check(press.Value == 1 && button.Bounds == bounds, "zoom press responds immediately without moving its hit target");
        bool changed = false;
        for (int pixelY = button.Height / 3; pixelY < button.Height * 2 / 3; pixelY++)
        for (int pixelX = button.Width / 3; pixelX < button.Width * 2 / 3; pixelX++)
            changed |= idle.GetPixel(pixelX, pixelY).ToArgb() != down.GetPixel(pixelX, pixelY).ToArgb();
        Check(changed, "zoom glyph changes visibly on pointer-down");
        idle.Save(Path.Combine(output, $"zoom-button-idle-{dpi}.png"), ImageFormat.Png);
        down.Save(Path.Combine(output, $"zoom-button-pressed-{dpi}.png"), ImageFormat.Png);
        Call(button, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, button.Width / 2, button.Height / 2, 0));
        await Task.Delay(35);
        if (UiMotion.Enabled) Check(press.IsRunning && press.Value > 0 && press.Value < 1, "zoom glyph recovers through intermediate spring frames");
        Call(button, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 2, 2, 0));
        button.Capture = false;
        Call(button, "OnMouseCaptureChanged", EventArgs.Empty);
        await Task.Delay(230);
        Check(press.Value == 0 && !press.IsRunning, "interrupted zoom press clears and stops its timer");
        if (button.Parent?.Parent is PreviewCanvas viewport)
        {
            viewport.SetZoomBarHovered(true);
            await WaitForFadeAsync((PreviewZoomFade)viewport.Controls.Find("previewZoomFade", true).Single());
        }
        int clicks = 0;
        EventHandler clicked = (_, _) => clicks++;
        button.Click += clicked;
        try { button.PerformClick(); Check(clicks == 1, "zoom command delivery remains immediate and synchronous"); }
        finally { button.Click -= clicked; }
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/preview-zoom"); Directory.CreateDirectory(output);
        PreviewCanvas preview = Field<PreviewCanvas>(main, "preview");
        bool originalMotion = UiMotion.UserEnabled;
        ApplicationPreferences preferences = Field<ApplicationPreferences>(main, "preferences");
        AppearanceMode originalAppearance = preferences.Appearance;
        Size originalSize = main.Size;
        try
        {
            main.TopMost = true;
            main.Activate();
            Check(!preview.Controls.Find("previewZoomBar", true).Single().Visible, "empty preview has no zoom controls");
            Bitmap source = Document();
            Call(main, "SetPreview", source);
            await Task.Delay(230);
            await VerifyHoverAsync(main, source, preview.Size, output);
            VerifyTransparency(main, output);
            preview.SetZoomBarHovered(true);
            await WaitForFadeAsync((PreviewZoomFade)preview.Controls.Find("previewZoomFade", true).Single());
            UiMotion.UserEnabled = false;
            preview.SetZoomBarHovered(true);
            preview.FitImageWidth();
            preview.Update();
            Control liveBar = preview.Controls.Find("previewZoomBar", true).Single();
            Control livePlus = liveBar.Controls.Find("previewZoomIn", true).Single();
            int liveBackgroundPaints = 0;
            PaintEventHandler trackBackground = (_, _) => liveBackgroundPaints++;
            livePlus.Paint += trackBackground;
            try
            {
                for (int frame = 0; frame < 8; frame++)
                {
                    int beforePaints = liveBackgroundPaints;
                    preview.ScrollPreview(40);
                    preview.Update();
                    Check(liveBackgroundPaints > beforePaints, "native zoom background follows scroll frame " + frame + " without waiting for queued child paint");
                }
                Point grab = new(preview.Width / 3, preview.Height / 3);
                Call(preview, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, grab.X, grab.Y, 0));
                try
                {
                    for (int frame = 1; frame <= 8; frame++)
                    {
                        int beforePaints = liveBackgroundPaints;
                        Call(preview, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, grab.X, grab.Y - frame * 12, 0));
                        preview.Update();
                        Check(liveBackgroundPaints > beforePaints, "native zoom background follows drag frame " + frame + " synchronously");
                    }
                }
                finally { Call(preview, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, grab.X, grab.Y - 96, 0)); }
            }
            finally { livePlus.Paint -= trackBackground; }
            preview.FitImage();
            double fit = Math.Min((double)preview.Width / source.Width, (double)preview.Height / source.Height);
            Check(Math.Abs(preview.ZoomFactor - fit) < .000001, "new capture starts fitted to the window");
            UiMotion.UserEnabled = false;
            // First enlarge past both bounds, then test the source point under a noncentral mouse anchor.
            preview.FitImageWidth();
            preview.ZoomBy(6, new PointF(preview.Width / 2f, preview.Height / 2f));
            Point anchor = new(preview.Width / 3, preview.Height / 3);
            RectangleF before = preview.ImageBounds;
            double sourceX = (anchor.X - before.X) / preview.ZoomFactor;
            double sourceY = (anchor.Y - before.Y) / preview.ZoomFactor;
            double scaleBefore = preview.ZoomFactor;
            Call(preview, "OnMouseWheel", new HandledMouseEventArgs(MouseButtons.None, 0, anchor.X, anchor.Y, 60));
            Check(Math.Abs(preview.ZoomFactor / scaleBefore - Math.Sqrt(1.2)) < .000001, "high-resolution wheel deltas scale proportionally");
            Check(Math.Abs((anchor.X - preview.ImageBounds.X) / preview.ZoomFactor - sourceX) < .01 &&
                Math.Abs((anchor.Y - preview.ImageBounds.Y) / preview.ZoomFactor - sourceY) < .01, "wheel keeps the source pixel under the pointer");
            main.Activate(); main.BringToFront();
            main.Controls.Find("copyButton", true).Single().Focus();
            Point hover = preview.PointToScreen(anchor);
            ShareX.HelpersLib.InputHelpers.SendMouseMove(hover);
            await Task.Delay(60);
            double hoveredScale = preview.ZoomFactor;
            PostMessage(main.Handle, 0x020A, new IntPtr(120 << 16), new IntPtr((hover.Y << 16) | (hover.X & 0xffff)));
            await Task.Delay(80);
            Console.WriteLine($"HOVER: hit={Control.FromChildHandle(WindowFromPoint(hover))?.Name}, focus={main.ActiveControl?.Name}");
            Check(preview.ZoomFactor > hoveredScale, "hover wheel reaches preview while copy button has keyboard focus");

            Call(preview, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, anchor.X, anchor.Y, 0));
            RectangleF grabbed = preview.ImageBounds;
            Call(preview, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, anchor.X + 16, anchor.Y + 23, 0));
            Call(preview, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, anchor.X + 16, anchor.Y + 23, 0));
            Check(Math.Abs(preview.ImageBounds.X - grabbed.X - 16) < .01 && Math.Abs(preview.ImageBounds.Y - grabbed.Y - 23) < .01,
                "drag pans at one-to-one pointer speed");
            preview.ZoomBy(10000, anchor); preview.ZoomBy(10000, anchor); preview.ZoomBy(10000, anchor);
            Check(preview.ZoomFactor <= 8, "zoom-in limit is 800 percent");
            for (int i = 0; i < 12; i++) preview.ZoomBy(-12, anchor);
            Check(preview.ZoomFactor > 0 && preview.ZoomFactor <= fit, "zoom-out reaches fit even for a very tall capture");

            ((Button)preview.Controls.Find("previewFitWidth", true).Single()).PerformClick();
            Check(preview.IsWidthReading && Math.Abs(preview.ImageBounds.Width - (preview.Width - 20 * preview.DeviceDpi / 96f)) < .1,
                "fit-width fills the usable pane without horizontal scrolling");
            Check(Math.Abs(preview.ImageBounds.Y) < .01, "long-image reading opens at the top");
            double readingScale = preview.ZoomFactor;
            Call(preview, "OnMouseWheel", new HandledMouseEventArgs(MouseButtons.None, 0, anchor.X, anchor.Y, -120));
            Check(preview.ImageBounds.Y < 0 && preview.ZoomFactor == readingScale, "reading wheel moves downward without changing scale");
            double y = preview.ImageBounds.Y;
            Call(preview, "OnKeyDown", new KeyEventArgs(Keys.PageDown));
            Check(preview.ImageBounds.Y < y - preview.Height * .8, "Page Down advances almost one viewport with context overlap");
            Call(preview, "OnKeyDown", new KeyEventArgs(Keys.End));
            Check(preview.ImageBounds.Bottom < preview.Height - 40 * preview.DeviceDpi / 96f,
                "last pixels can scroll above the floating toolbar");
            Call(preview, "OnKeyDown", new KeyEventArgs(Keys.Home));
            Check(preview.ImageBounds.Y == 0, "Home returns to the beginning");
            preview.ScrollPreview(500);
            main.Width -= 80; await Task.Delay(60);
            Check(preview.IsWidthReading && Math.Abs(preview.ImageBounds.Width - (preview.Width - 20 * preview.DeviceDpi / 96f)) < .1,
                "reading refits to width after resize");
            main.Size = originalSize;

            foreach (AppearanceMode appearance in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                preferences.Appearance = appearance; Call(main, "ApplyAppearance"); UiMotion.UserEnabled = false;
                preview.FitImageWidth(); preview.ScrollPreview(700);
                await Task.Delay(80);
                using Bitmap shot = new(main.Width, main.Height); main.DrawToBitmap(shot, new Rectangle(Point.Empty, main.Size));
                shot.Save(Path.Combine(output, $"{appearance}-{main.DeviceDpi}.png"), ImageFormat.Png);
                using (Bitmap live = new ShareX.ScreenCaptureLib.Screenshot().CaptureWindow(main.Handle))
                    live.Save(Path.Combine(output, $"{appearance}-desktop-{main.DeviceDpi}.png"), ImageFormat.Png);
                Control bar = preview.Controls.Find("previewZoomBar", true).Single();
                Check(bar.BackColor == (appearance == AppearanceMode.Dark ? CapturePalette.Dark.Surface : CapturePalette.Light.Surface),
                    appearance + " floating controls use the current theme");
                Check(bar.Controls.Cast<Control>().All(c => c.Width >= 28 * main.DeviceDpi / 96f && !string.IsNullOrWhiteSpace(c.AccessibleName)),
                    appearance + " controls have desktop hit targets and accessible names");
            }
            main.Size = main.MinimumSize; await Task.Delay(80);
            Control compactBar = preview.Controls.Find("previewZoomBar", true).Single();
            Check(preview.ClientRectangle.Contains(compactBar.Bounds), "floating controls fit in the minimum-size window");
            using (Bitmap shot = new(main.Width, main.Height))
            {
                main.DrawToBitmap(shot, new Rectangle(Point.Empty, main.Size));
                shot.Save(Path.Combine(output, $"compact-{main.DeviceDpi}.png"), ImageFormat.Png);
            }
            main.Size = originalSize;
            UiMotion.UserEnabled = true;
            preview.FitImage(); await Settle(preview);
            if (UiMotion.Enabled)
            {
                preview.ZoomBy(3, anchor); await Task.Delay(35);
                double mid = preview.ZoomFactor;
                preview.ZoomBy(-2, anchor);
                Check(Math.Abs(preview.ZoomFactor - mid) < .000001, "reversing zoom preserves the current on-screen scale");
                await Settle(preview);
                preview.ZoomBy(2, anchor); preview.Hide();
                Check(!preview.IsViewportAnimating, "hidden preview stops animation scheduling");
                preview.Show();
            }
            UiMotion.UserEnabled = false; preview.FitImageWidth();
            Check(!preview.IsViewportAnimating, "reduced motion applies width mode immediately");
            Check(ReferenceEquals(preview.Image, source) && ReferenceEquals(Field<Bitmap>(main, "result"), source)
                && source.Size == new Size(1100, 8800) && source.GetPixel(500, 8790).ToArgb() == Color.Coral.ToArgb(),
                "zoom and reading retain the original full-resolution capture and final pixels");
            Bitmap replacement = new(300, 200); Call(main, "SetPreview", replacement);
            Check(!preview.IsWidthReading && !preview.IsViewportAnimating && Math.Abs(preview.ZoomFactor - Math.Min((double)preview.Width / 300, (double)preview.Height / 200)) < .000001,
                "next capture resets reading and zoom state");
            foreach (string culture in new[] { "en", "zh-CN", "zh-HK" })
            foreach (string key in new[] { "PreviewZoomControls", "PreviewZoomIn", "PreviewZoomOut", "PreviewFitWidth", "PreviewFitImage", "PreviewReadingHelp" })
                Check(!string.IsNullOrWhiteSpace(Strings.ResourceManager.GetString(key, System.Globalization.CultureInfo.GetCultureInfo(culture))), culture + " " + key + " is translated");
        }
        finally
        {
            main.TopMost = false;
            main.Size = originalSize; preferences.Appearance = originalAppearance; Call(main, "ApplyAppearance");
            UiMotion.UserEnabled = originalMotion;
        }
    }
}
