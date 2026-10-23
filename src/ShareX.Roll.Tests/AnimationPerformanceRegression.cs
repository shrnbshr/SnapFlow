using ShareX.Roll;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class AnimationPerformanceRegression
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Check(bool condition, string text)
    {
        if (!condition) throw new InvalidOperationException(text);
        Console.WriteLine("PASS: " + text);
    }

    private static Bitmap Pattern()
    {
        Bitmap image = new(67, 43, PixelFormat.Format32bppPArgb);
        using Graphics g = Graphics.FromImage(image);
        g.Clear(Color.FromArgb(30, 80, 150));
        g.FillRectangle(Brushes.Coral, 4, 6, 22, 29);
        g.FillRectangle(Brushes.WhiteSmoke, 29, 3, 30, 13);
        return image;
    }

    private static void VerifyPixels()
    {
        using Bitmap source = Pattern();
        foreach (float opacity in new[] { 0f, .37f, .82f, 1f })
        foreach (int x in new[] { -18, 0, 18 })
        foreach (bool transformed in new[] { false, true })
        {
            using Bitmap actual = new(91, 63), expected = new(91, 63);
            using MotionFrame frame = new((Bitmap)source.Clone());
            using MotionFrame surface = new(actual.Size);
            Check(Field<int[]?>(frame, "pixels") != null, "opaque display pixels are available");
            using (Graphics a = Graphics.FromImage(actual))
            using (Graphics b = Graphics.FromImage(expected))
            {
                a.Clear(Color.FromArgb(240, 230, 220)); b.Clear(Color.FromArgb(240, 230, 220));
                if (transformed) { a.TranslateTransform(7, 5); b.TranslateTransform(7, 5); }
                a.SetClip(new Rectangle(3, 2, 77, 51)); b.SetClip(new Rectangle(3, 2, 77, 51));
                surface.Compose(a, Color.FromArgb(240, 230, 220), frame, x, opacity);
                using ImageAttributes attributes = new();
                attributes.SetColorMatrix(new ColorMatrix { Matrix33 = opacity });
                b.DrawImage(source, new Rectangle(x, 0, source.Width, source.Height), 0, 0,
                    source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            int maximum = 0;
            string worst = "";
            for (int y = 0; y < actual.Height; y++)
            for (int px = 0; px < actual.Width; px++)
            {
                Color a = actual.GetPixel(px, y), b = expected.GetPixel(px, y);
                int error = Math.Max(Math.Abs(a.R-b.R), Math.Max(Math.Abs(a.G-b.G), Math.Abs(a.B-b.B)));
                if (error > maximum) { maximum = error; worst = $" at {px},{y}: {a} vs {b}"; }
            }
            Check(maximum <= 2, $"frame opacity={opacity}, offset={x}, transform={transformed} preserves pixels and clipping (max error {maximum}{worst})");
        }
        int before = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
        for (int i = 0; i < 200; i++) { using MotionFrame frame = new(Pattern()); }
        int after = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
        Check(after <= before + 2, $"200 frame lifetimes release their drawing resources ({before} -> {after})");
        using MotionFrame pinned = new(Pattern());
        using MotionFrame target = new(pinned.Size);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        using Bitmap result = new(pinned.Size.Width, pinned.Size.Height);
        using (Graphics graphics = Graphics.FromImage(result)) target.Compose(graphics, Color.Black, pinned);
        Check(result.GetPixel(5, 7).ToArgb() == Color.Coral.ToArgb(), "the bitmap view remains valid through a compacting GC");
        pinned.Dispose();
        Check(Field<int[]?>(pinned,"pixels") == null && Field<Bitmap?>(pinned,"bitmap") == null,
            "disposing a frame clears both its bitmap and pinned-array references");
    }

    internal static async Task RunAsync(MainForm main)
    {
        VerifyPixels();
        bool original = UiMotion.UserEnabled;
        using Form host = new() { ClientSize = new Size(380, 310), Text = "Animation rendering regression" };
        using PreviewCanvas preview = new() { Bounds = new Rectangle(10, 10, 340, 280), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
        host.Controls.Add(preview);
        using Bitmap image = new(1800, 4200);
        using (Graphics graphics = Graphics.FromImage(image)) { graphics.Clear(Color.White); graphics.FillRectangle(Brushes.Coral, 100, 100, 1200, 2800); }
        try
        {
            UiMotion.UserEnabled = true;
            host.Show(main);
            bool allowed = UiMotion.Enabled;
            preview.Image = image;
            preview.Refresh();
            UiTween reveal = Field<UiTween>(preview, "reveal");
            if (allowed)
            {
                Check(Field<MotionFrame?>(preview, "revealFrame") != null, "fade uses a temporary display-sized frame");
                Check(reveal.Value >= .82f, "the first image frame remains at least 82 percent visible");
                await Task.Delay(260);
                Check(reveal.Value == 1 && !reveal.IsRunning && Field<MotionFrame?>(preview, "revealFrame") == null,
                    "completed fade releases its display copy and stops scheduling");
                preview.Image = null; preview.Refresh(); preview.Image = image; preview.Refresh();
                preview.Width -= 10;
                Check(!reveal.IsRunning && Field<MotionFrame?>(preview, "revealFrame") == null, "resize releases the old-size fade copy");
                preview.Image = null; preview.Refresh(); preview.Image = image; preview.Refresh();
                preview.Hide();
                Check(!reveal.IsRunning && Field<MotionFrame?>(preview, "revealFrame") == null, "hidden preview releases its fade copy");
                preview.Show();
            }
            UiMotion.UserEnabled = false;
            preview.Image = null; preview.Refresh(); preview.Image = image; preview.Refresh();
            Check(!reveal.IsRunning && Field<MotionFrame?>(preview, "revealFrame") == null, "reduced motion does not allocate an animation copy");
            Check(ReferenceEquals(preview.Image, image) && image.GetPixel(110, 110).R == Color.Coral.R,
                "presentation caching preserves the full-resolution source image");
            using PictureBox reference = new() { Size = preview.Size, SizeMode = preview.SizeMode, BackColor = preview.BackColor, Image = image };
            using Bitmap actual = new(preview.Width, preview.Height), expected = new(preview.Width, preview.Height);
            preview.DrawToBitmap(actual, preview.ClientRectangle);
            reference.DrawToBitmap(expected, reference.ClientRectangle);
            // Rounded corners are clipped by PreviewCanvas, so compare inside the image's central area.
            bool same = true;
            for (int y = 20; y < actual.Height-20; y += 7)
            for (int x = 20; x < actual.Width-20; x += 7)
            {
                // The new floating zoom controls are deliberately above the PictureBox image.
                if (preview.Controls.Cast<Control>().Any(c => c.Visible && c.Bounds.Contains(x, y))) continue;
                same &= actual.GetPixel(x,y).ToArgb() == expected.GetPixel(x,y).ToArgb();
            }
            Check(same, "settled preview retains PictureBox's image layout and quality");
            preview.Image = null;
        }
        finally { UiMotion.UserEnabled = original; host.Close(); }
        await VerifyNavigation(main);
    }

    private static void Call(MainForm main, string name) => typeof(MainForm).GetMethod(name,
        BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);

    private static Bitmap Snapshot(Control control)
    {
        Bitmap image = new(control.Width, control.Height);
        control.DrawToBitmap(image, control.ClientRectangle);
        TransitionSnapshot.RestoreNativeTextColors(control, image);
        return image;
    }

    private static void Compare(Bitmap actual, Bitmap expected, string text)
    {
        Check(actual.Size == expected.Size, text + " keeps the page size");
        int changed = 0, total = 0;
        for (int y = 0; y < actual.Height; y += 3)
        for (int x = 0; x < actual.Width; x += 3)
        {
            Color a = actual.GetPixel(x,y), b = expected.GetPixel(x,y);
            if (Math.Max(Math.Abs(a.R-b.R), Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B))) > 3) changed++;
            total++;
        }
        Check((double)changed / total < .005, $"{text} is visually continuous ({changed}/{total} samples differ)");
    }

    private static async Task VerifyNavigation(MainForm main)
    {
        bool originalMotion = UiMotion.UserEnabled;
        ApplicationPreferences preferences = Field<ApplicationPreferences>(main, "preferences");
        AppearanceMode originalAppearance = preferences.Appearance;
        bool originalPreference = preferences.AnimationsEnabled;
        try
        {
            preferences.AnimationsEnabled = true;
            foreach (AppearanceMode appearance in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            {
                preferences.Appearance = appearance;
                Call(main, "ApplyAppearance");
                if (!UiMotion.Enabled) continue;
                foreach (string direction in new[] { "OpenSettings", "CloseSettings" })
                {
                    Control beforePage = direction == "OpenSettings" ? Field<Control>(main,"workspace") : Field<Control>(main,"settingsPage");
                    using Bitmap before = Snapshot(beforePage);
                    Call(main, direction);
                    PageTransition transition = Field<PageTransition>(main, "pageTransition");
                    UiTween progress = Field<UiTween>(transition, "progress");
                    progress.SnapTo(0);
                    using Bitmap first = Snapshot(transition);
                    Compare(first, before, appearance + " " + direction + " first frame");
                    progress.SnapTo(1);
                    using Bitmap last = Snapshot(transition);
                    Call(main, "EndPageTransition");
                    Control afterPage = direction == "OpenSettings" ? Field<Control>(main,"settingsPage") : Field<Control>(main,"workspace");
                    using Bitmap after = Snapshot(afterPage);
                    Compare(last, after, appearance + " " + direction + " last frame");
                    await Task.Delay(20);
                }
                Call(main,"OpenSettings");
                PageTransition outgoing = Field<PageTransition>(main,"pageTransition");
                Field<UiTween>(outgoing,"progress").SnapTo(.35f);
                using Bitmap intermediate = Snapshot(outgoing);
                Call(main,"CloseSettings");
                PageTransition incoming = Field<PageTransition>(main,"pageTransition");
                Field<UiTween>(incoming,"progress").SnapTo(0);
                using Bitmap reversed = Snapshot(incoming);
                Compare(reversed, intermediate, appearance + " interrupted navigation");
                Call(main,"EndPageTransition");
            }
            UiMotion.UserEnabled = false;
            Call(main,"OpenSettings");
            Check(Field<PageTransition?>(main,"pageTransition") == null, "reduced-motion navigation allocates no animation cover");
            Call(main,"CloseSettings");
            Check(Field<Control>(main,"workspace").Visible, "reduced-motion return restores the capture workspace");
        }
        finally
        {
            Call(main,"CloseSettings");
            Call(main,"EndPageTransition");
            preferences.Appearance = originalAppearance;
            preferences.AnimationsEnabled = originalPreference;
            Call(main,"ApplyAppearance");
            UiMotion.UserEnabled = originalMotion;
        }
    }

    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flags);
}
