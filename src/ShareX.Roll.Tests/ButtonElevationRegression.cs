using ShareX.Roll;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.Loader;

internal static class ButtonElevationRegression
{
    private static int checks;
    private static T Field<T>(object value, string name) => (T)value.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value)!;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
        Console.WriteLine("PASS: " + message);
    }
    private static bool Equal(Bitmap a, Bitmap b)
    {
        if (a.Size != b.Size) return false;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }
    private static int TextTop(Bitmap image, Color ink)
    {
        for (int y = 0; y < image.Height; y++)
            for (int x = image.Width / 4; x < image.Width * 3 / 4; x++)
            {
                Color pixel = image.GetPixel(x, y);
                if (Math.Abs(pixel.R - ink.R) < 6 && Math.Abs(pixel.G - ink.G) < 6 && Math.Abs(pixel.B - ink.B) < 6)
                    return y;
            }
        throw new InvalidOperationException("Cannot find button label pixels");
    }
    private static int TextDifference(Bitmap a, Bitmap b, Rectangle? region = null)
    {
        int maximum = 0;
        Rectangle area = region ?? new Rectangle(a.Width / 5, a.Height / 5, a.Width * 3 / 5, a.Height * 3 / 5);
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                Color first = a.GetPixel(x, y), second = b.GetPixel(x, y);
                maximum = Math.Max(maximum, Math.Max(Math.Abs(first.R - second.R),
                    Math.Max(Math.Abs(first.G - second.G), Math.Abs(first.B - second.B))));
            }
        return maximum;
    }
    private static int NativeTextDifference(Bitmap image, Button button, Font font, Color ink, float scale)
    {
        using Bitmap reference = new(image.Width, image.Height);
        reference.SetResolution(button.DeviceDpi, button.DeviceDpi);
        using (Graphics g = Graphics.FromImage(reference))
        {
            g.Clear(image.GetPixel(image.Width / 2, (int)(ButtonElevation.FaceBounds(button.ClientSize, scale).Top + 2 * scale)));
            TextRenderer.DrawText(g, button.Text, font, button.ClientRectangle, ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | UiLanguage.TextDirection);
        }
        Size text = TextRenderer.MeasureText(button.Text, font);
        return TextDifference(image, reference, new Rectangle((image.Width - text.Width) / 2,
            (image.Height - text.Height) / 2, text.Width, text.Height));
    }

    internal static void Run(bool baseline, bool preview = false)
    {
        string output = Path.GetFullPath("artifacts/button-elevation");
        Directory.CreateDirectory(output);
        bool originalMotion = UiMotion.UserEnabled;
        try
        {
            UiMotion.UserEnabled = true;
            VerifySharedShadow(output);
            VerifyShadowBounds();
            VerifyEditorShadow(output);
            foreach (CapturePalette palette in new[] { CapturePalette.Light, CapturePalette.Dark })
            foreach (string kind in new[] { "quiet", "surface", "primary", "disabled" })
            {
                using Panel parent = new() { Size = new Size(220, 70), BackColor = palette.Background };
                using Font font = new("Microsoft YaHei UI", 11);
                using Button button = new() { Text = "保存更改", Name = "sample", Font = font, Tag = kind == "quiet" ? "quiet" : null };
                float scale = button.DeviceDpi / 96f;
                button.Bounds = new Rectangle(0, 0, (int)(196 * scale), (int)(68 * scale));
                button.Enabled = kind != "disabled";
                parent.Controls.Add(button);
                palette.StyleButton(button, kind == "primary");
                object states = typeof(CaptureButtonPainter).GetField("states", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                object?[] values = [button, null];
                states.GetType().GetMethod("TryGetValue")!.Invoke(states, values);
                object state = values[1]!;
                UiTween hover = Field<UiTween>(state, "Hover"), press = Field<UiTween>(state, "Press");
                string name = (palette == CapturePalette.Light ? "Light-" : "Dark-") + kind + "-" + button.DeviceDpi;
                Bitmap Render(bool matte = false)
                {
                    Bitmap image = new(button.Width, button.Height);
                    image.SetResolution(button.DeviceDpi, button.DeviceDpi);
                    using Graphics g = Graphics.FromImage(image);
                    typeof(CaptureButtonPainter).GetMethod("Paint", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, [button, state, g, matte]);
                    return image;
                }
                using Bitmap idle = Render();
                string before = Path.Combine(output, "before-idle-" + name + ".png");
                if (baseline) idle.Save(before, ImageFormat.Png);
                else Check(button.FlatAppearance.BorderSize == 0, name + ": native decorative border is disabled");
                Rectangle bounds = button.Bounds;
                Font originalFont = button.Font;
                hover.SnapTo(1);
                using Bitmap raised = Render();
                raised.Save(Path.Combine(output, (baseline ? "before-hover-" : "after-hover-") + name + ".png"), ImageFormat.Png);
                if (baseline) continue;

                Check(button.Bounds == bounds, name + ": hover preserves layout and native hit target");
                if (!button.Enabled)
                {
                    Check(Equal(idle, raised), name + ": disabled button has no elevation");
                    continue;
                }
                hover.SnapTo(.0001f);
                using (Bitmap initial = Render())
                    Check(TextDifference(idle, initial) <= 2, name + ": subpixel rendering starts without a text-weight jump");
                hover.SnapTo(1);
                Check(!Equal(raised, idle), name + ": hover adds visible feedback");
                if (palette == CapturePalette.Dark)
                {
                    int bottom = (int)(ButtonElevation.FaceBounds(button.ClientSize, scale).Top - 2 * scale);
                    bool brightened = false;
                    for (int y = 0; y < bottom; y++)
                    {
                        Color pixel = raised.GetPixel(raised.Width / 2, y);
                        brightened |= pixel.R > parent.BackColor.R || pixel.G > parent.BackColor.G || pixel.B > parent.BackColor.B;
                    }
                    Check(!brightened, name + ": the area above a dark button has no bright halo");
                }
                if (UiMotion.Enabled)
                {
                    Color shadow = raised.GetPixel(raised.Width / 2,
                        (int)(ButtonElevation.FaceBounds(button.ClientSize, scale).Bottom + 2 * scale));
                    Check(shadow.R < parent.BackColor.R && shadow.G < parent.BackColor.G && shadow.B < parent.BackColor.B,
                        name + ": a soft shadow is visible below the raised face");
                }
                Color ink = kind == "primary" ? palette.AccentText : palette.Text;
                Check(TextTop(raised, ink) == TextTop(idle, ink), name + ": hover keeps text at its original position");
                foreach (float amount in new[] { .25f, .5f, .75f, 1f })
                {
                    hover.SnapTo(amount);
                    using Bitmap sample = Render();
                    int difference = NativeTextDifference(sample, button, originalFont, ink, scale);
                    Check(difference <= 2, name + $": text matches the original native font at hover {amount} (max pixel difference {difference})");
                }
                press.SnapTo(1);
                using Bitmap down = Render();
                down.Save(Path.Combine(output, "pressed-" + name + ".png"), ImageFormat.Png);
                Check(!Equal(raised, down), name + ": press settles the raised face");
                press.SnapTo(0);
                foreach (float amount in new[] { .1f, .5f, 1f })
                {
                    hover.SnapTo(amount);
                    using Bitmap normal = Render(), mask = Render(matte: true);
                    // The live Acrylic path and page-transition mask must agree at the shadow and rounded edge.
                    using Bitmap page = new(button.Width, button.Height);
                    using (Graphics g = Graphics.FromImage(page)) g.DrawImageUnscaled(normal, Point.Empty);
                    parent.Size = button.Size;
                    BackdropSnapshot snapshot = BackdropSnapshot.Capture(parent, page, palette);
                    foreach (Point point in new[] { new Point(button.Width / 2, button.Height - 1), new Point((int)(3 * scale), button.Height / 2) })
                    {
                        uint expected = BackdropSnapshot.CoveredMatte(BackdropSnapshot.RemovedMatte(palette),
                            BackdropSnapshot.Coverage((uint)mask.GetPixel(point.X, point.Y).ToArgb()));
                        Check(snapshot.Pixels[point.Y * page.Width + point.X] == expected, name + $": shadow material coverage agrees at hover {amount}");
                    }
                }
                UiMotion.UserEnabled = false;
                hover.SnapTo(1);
                using Bitmap reduced = Render();
                reduced.Save(Path.Combine(output, "reduced-motion-" + name + ".png"), ImageFormat.Png);
                Check(TextTop(reduced, ink) == TextTop(idle, ink), name + ": reduced motion keeps the label at its resting position");
                UiMotion.UserEnabled = true;
                hover.SnapTo(0);
                using Bitmap restored = Render();
                Check(Equal(idle, restored), name + ": leaving hover restores every resting pixel");
                Check(!button.IsHandleCreated && !parent.IsHandleCreated, "render checks do not open windows");
                Stopwatch timer = Stopwatch.StartNew();
                for (int frame = 0; frame < 40; frame++)
                {
                    hover.SnapTo(frame / 39f);
                    using Bitmap sample = Render();
                }
                timer.Stop();
                Check(timer.Elapsed.TotalMilliseconds / 40 < 16.7, name + $": cached drawing averages {timer.Elapsed.TotalMilliseconds / 40:F2} ms per frame");
                if (preview && kind == "quiet")
                {
                    string frames = Path.Combine(output, "frames-" + name);
                    Directory.CreateDirectory(frames);
                    for (int frame = 0; frame < 120; frame++)
                    {
                        // Sample the existing critically damped spring at 2.5 times slower playback for inspection.
                        float progress = 0;
                        if (frame >= 24 && frame < 48) progress = Spring((frame - 24) / 150f);
                        else if (frame >= 48 && frame < 72) progress = 1;
                        else if (frame >= 72 && frame < 96) progress = 1 - Spring((frame - 72) / 150f);
                        hover.SnapTo(progress);
                        using Bitmap sample = Render();
                        sample.Save(Path.Combine(frames, frame.ToString("D3") + ".png"), ImageFormat.Png);
                    }
                }
            }
            Console.WriteLine($"Button elevation: {checks} checks passed.");
        }
        finally { UiMotion.UserEnabled = originalMotion; }
    }

    private static float Spring(float seconds)
    {
        float frequency = 7f / (ButtonElevation.ResponseMilliseconds / 1000f);
        return 1 - (1 + frequency * seconds) * MathF.Exp(-frequency * seconds);
    }

    private static void VerifySharedShadow(string output)
    {
        using Button probe = new();
        float scale = probe.DeviceDpi / 96f;
        int D(int value) => (int)Math.Round(value * scale);
        foreach (bool dark in new[] { false, true })
        {
            using ButtonElevation elevation = new();
            Bitmap Render(bool compact, bool unified)
            {
                Bitmap image = new(D(260), D(compact ? 88 : 100));
                using Graphics graphics = Graphics.FromImage(image);
                graphics.Clear(Color.Transparent);
                elevation.PaintShadow(graphics, image.Size, scale, dark, 1, compact, unified);
                return image;
            }
            using Bitmap regular = Render(false, true), compact = Render(true, true), legacy = Render(false, false);
            regular.Save(Path.Combine(output, $"shared-regular-{dark}-{probe.DeviceDpi}.png"), ImageFormat.Png);
            compact.Save(Path.Combine(output, $"shared-compact-{dark}-{probe.DeviceDpi}.png"), ImageFormat.Png);
            int maximum = 0;
            for (int distance = 1; distance <= 5; distance++)
                maximum = Math.Max(maximum, Math.Abs(regular.GetPixel(D(16 - distance), regular.Height / 2).A -
                    compact.GetPixel(D(8 - distance), compact.Height / 2).A));
            Check(maximum <= 2, $"shared/{dark}: compact and regular shadows have the same logical falloff (maximum alpha difference {maximum})");
            bool upperClear = true;
            for (int y = 0; y <= D(12); y++)
                for (int x = 0; x < regular.Width; x++) upperClear &= regular.GetPixel(x, y).A == 0;
            Check(upperClear, $"shared/{dark}: the upper edge has no shadow halo");
            foreach (Bitmap image in new[] { regular, compact })
            {
                bool clear = true;
                for (int x = 0; x < image.Width; x++) clear &= image.GetPixel(x, 0).A == 0 && image.GetPixel(x, image.Height - 1).A == 0;
                for (int y = 0; y < image.Height; y++) clear &= image.GetPixel(0, y).A == 0 && image.GetPixel(image.Width - 1, y).A == 0;
                Check(clear, $"shared/{dark}/{image.Height}: the complete untrimmed shadow fits inside its native bounds");
            }
            using Bitmap restored = Render(false, true);
            Check(Equal(regular, restored), $"shared/{dark}: switching geometry or legacy editor mode does not reuse an incompatible shadow cache");
        }
    }

    private static void VerifyEditorShadow(string output)
    {
        string? reference = Environment.GetEnvironmentVariable("SNAPFLOW_SHADOW_REFERENCE");
        if (string.IsNullOrWhiteSpace(reference)) return;
        AssemblyLoadContext context = new("original-editor-shadow", isCollectible: true);
        try
        {
            Type type = context.LoadFromAssemblyPath(Path.GetFullPath(reference)).GetType("ShareX.Roll.ButtonElevation")!;
            MethodInfo paint = type.GetMethod("PaintShadow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using Button probe = new();
            float scale = probe.DeviceDpi / 96f;
            foreach (bool dark in new[] { false, true })
            foreach (Size logical in new[] { new Size(44, 44), new Size(168, 52), new Size(260, 52) })
            {
                Size size = new((int)(logical.Width * scale), (int)(logical.Height * scale));
                using Bitmap before = new(size.Width, size.Height), after = new(size.Width, size.Height);
                using IDisposable original = (IDisposable)Activator.CreateInstance(type, nonPublic: true)!;
                using ButtonElevation current = new();
                using (Graphics graphics = Graphics.FromImage(before))
                {
                    graphics.Clear(Color.Transparent);
                    paint.Invoke(original, [graphics, size, scale, dark, 1f, true]);
                }
                using (Graphics graphics = Graphics.FromImage(after))
                {
                    graphics.Clear(Color.Transparent);
                    current.PaintShadow(graphics, size, scale, dark, 1, compact: true);
                }
                Check(Equal(before, after), $"editor/{dark}/{logical}: original editor shadow pixels are preserved");
                after.Save(Path.Combine(output, $"editor-preserved-{dark}-{logical.Width}-{probe.DeviceDpi}.png"), ImageFormat.Png);
            }
        }
        finally { context.Unload(); }
    }

    private static void VerifyShadowBounds()
    {
        foreach (int dpi in new[] { 96, 120, 144, 168, 192, 240, 288, 384 })
        foreach (bool compact in new[] { false, true })
        foreach (Size logical in compact ? new[] { new Size(44, 44), new Size(232, 52), new Size(260, 88) } : new[] { new Size(144, 64), new Size(248, 100) })
        {
            float scale = dpi / 96f;
            Size size = new((int)(logical.Width * scale), (int)(logical.Height * scale));
            using Bitmap image = new(size.Width, size.Height);
            using ButtonElevation shadow = new();
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.Transparent);
                shadow.PaintShadow(graphics, size, scale, dark: true, 1, compact, unified: true);
            }
            bool clear = true;
            for (int x = 0; x < size.Width; x++) clear &= image.GetPixel(x, 0).A == 0 && image.GetPixel(x, size.Height - 1).A == 0;
            for (int y = 0; y < size.Height; y++) clear &= image.GetPixel(0, y).A == 0 && image.GetPixel(size.Width - 1, y).A == 0;
            Check(clear, $"bounds/{dpi}/{logical}: the natural blur tail ends before any control edge");
        }
    }
}
