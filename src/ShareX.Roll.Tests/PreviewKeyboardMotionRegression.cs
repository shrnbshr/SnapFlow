using ShareX.Roll;
using System.Reflection;

internal static class PreviewKeyboardMotionRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Keys[] HandledKeys = [Keys.Add, Keys.Oemplus, Keys.Subtract, Keys.OemMinus,
        Keys.D0, Keys.NumPad0, Keys.W, Keys.Up, Keys.Down, Keys.PageUp, Keys.PageDown, Keys.Space,
        Keys.Home, Keys.End, Keys.Left, Keys.Right];

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private static void Near(double actual, double expected, string message) =>
        Check(Math.Abs(actual - expected) < .001, message);

    private static (double Value, double Target, double Velocity) Axis(PreviewCanvas preview, string name)
    {
        object axis = typeof(PreviewCanvas).GetField(name, Private)!.GetValue(preview)!;
        double Read(string field) => (double)axis.GetType().GetField(field, Private)!.GetValue(axis)!;
        return (Read("Value"), Read("Target"), Read("Velocity"));
    }

    private static KeyEventArgs SendKey(PreviewCanvas preview, Keys key)
    {
        KeyEventArgs args = new(key);
        typeof(PreviewCanvas).GetMethod("OnKeyDown", Private)!.Invoke(preview, [args]);
        return args;
    }

    private static void Press(PreviewCanvas preview, Keys key)
    {
        KeyEventArgs args = SendKey(preview, key);
        Check(args.Handled && args.SuppressKeyPress && !preview.IsViewportAnimating &&
            new[] { "zoom", "offsetX", "offsetY" }.All(name =>
            {
                var axis = Axis(preview, name);
                return axis.Value == axis.Target && axis.Velocity == 0;
            }), key + " commits every viewport axis and stops motion in the same input event");
    }

    internal static async Task RunAsync(MainForm main)
    {
        bool originalMotion = UiMotion.UserEnabled;
        using Bitmap image = new(1600, 7200);
        image.SetPixel(0, 0, Color.Coral);
        image.SetPixel(image.Width - 1, image.Height - 1, Color.CornflowerBlue);
        using PreviewCanvas preview = new()
        {
            Name = "keyboardMotionPreview", Location = new Point(-10000, -10000),
            Size = new Size(520, 340), Image = image, SizeMode = PictureBoxSizeMode.Zoom
        };
        main.Controls.Add(preview);
        try
        {
            preview.SynchronizeImage();
            // Exercise the same keyboard contract with normal motion and reduced motion.
            foreach (bool enabled in new[] { true, false })
            {
                UiMotion.UserEnabled = enabled;
                double dpi = preview.DeviceDpi / 96d;
                double fit = Math.Min((double)preview.Width / image.Width, (double)preview.Height / image.Height);
                double width = (preview.Width - 20 * dpi) / image.Width;
                double page = preview.Height * .9;
                double row = 44 * dpi;
                Press(preview, Keys.W);
                Near(preview.ZoomFactor, width, "W applies exact fit-width scale");
                Near(preview.ImageBounds.Y, 0, "W opens reading at the top");

                foreach (Keys key in new[] { Keys.Add, Keys.Oemplus, Keys.Subtract, Keys.OemMinus })
                {
                    double before = preview.ZoomFactor;
                    Press(preview, key);
                    Near(preview.ZoomFactor, before * (key is Keys.Add or Keys.Oemplus ? 1.2 : 1 / 1.2),
                        key + " applies the existing zoom step immediately");
                }
                foreach (Keys key in new[] { Keys.D0, Keys.NumPad0 })
                {
                    Press(preview, key);
                    Near(preview.ZoomFactor, fit, key + " restores exact full-image fit");
                }
                Press(preview, Keys.W);
                Press(preview, Keys.Down); Near(preview.ImageBounds.Y, -row, "Down advances one keyboard row");
                Press(preview, Keys.Up); Near(preview.ImageBounds.Y, 0, "Up reverses the keyboard row");
                Press(preview, Keys.PageDown); Near(preview.ImageBounds.Y, -page, "Page Down retains context overlap");
                Press(preview, Keys.Space); Near(preview.ImageBounds.Y, -2 * page, "Space advances one page");
                Press(preview, Keys.PageUp); Near(preview.ImageBounds.Y, -page, "Page Up reverses one page");
                Press(preview, Keys.End);
                PreviewZoomBar bar = (PreviewZoomBar)preview.Controls.Find("previewZoomBar", true).Single();
                Near(preview.ImageBounds.Bottom, preview.Height - bar.FaceBounds.Height - 24 * dpi,
                    "End exposes the last source pixels above the floating toolbar");
                Press(preview, Keys.Home); Near(preview.ImageBounds.Y, 0, "Home reaches the start immediately");

                // Enlarge enough that both horizontal directions have room to move.
                Press(preview, Keys.Add); Press(preview, Keys.Add); Press(preview, Keys.Add);
                double x = preview.ImageBounds.X;
                Press(preview, Keys.Right); Near(preview.ImageBounds.X, x - row, "Right moves by the existing keyboard row distance");
                Press(preview, Keys.Left); Near(preview.ImageBounds.X, x, "Left immediately reverses Right");
                Press(preview, Keys.W);
                for (int i = 0; i < 12; i++) { Press(preview, Keys.Down); Press(preview, Keys.Up); }
                Near(preview.ImageBounds.Y, 0, "rapid opposite keys never accumulate pending displacement");

                var beforeUnhandled = (Axis(preview, "zoom"), Axis(preview, "offsetX"), Axis(preview, "offsetY"));
                KeyEventArgs unhandled = SendKey(preview, Keys.F12);
                Check(!unhandled.Handled && !unhandled.SuppressKeyPress && beforeUnhandled ==
                    (Axis(preview, "zoom"), Axis(preview, "offsetX"), Axis(preview, "offsetY")),
                    "an unrelated key retains its native handling and viewport state");
                Check(UiMotion.UserEnabled == enabled, "keyboard input leaves the global motion preference unchanged");
            }

            UiMotion.UserEnabled = true;
            if (UiMotion.Enabled)
            {
                PointF center = new(preview.Width / 2f, preview.Height / 2f);
                foreach (Keys key in HandledKeys)
                {
                    Press(preview, Keys.W);
                    preview.ZoomBy(2, center);
                    Check(preview.IsViewportAnimating && preview.ZoomFactor != preview.ZoomTarget,
                        "pointer zoom still starts a continuous spring before " + key);
                    Press(preview, key);
                    var committed = (Axis(preview, "zoom"), Axis(preview, "offsetX"), Axis(preview, "offsetY"));
                    await Task.Delay(35);
                    Check(!preview.IsViewportAnimating && committed ==
                        (Axis(preview, "zoom"), Axis(preview, "offsetX"), Axis(preview, "offsetY")),
                        key + " cancels pending pointer frames without later movement");
                }
                Press(preview, Keys.W);
                preview.ScrollPreview(500);
                Check(preview.IsViewportAnimating, "pointer reading scroll retains its spring");
                var pending = Axis(preview, "offsetY");
                KeyEventArgs ignored = SendKey(preview, Keys.F12);
                Check(!ignored.Handled && !ignored.SuppressKeyPress && preview.IsViewportAnimating &&
                    Axis(preview, "offsetY") == pending, "an unrelated key does not interrupt pointer motion");
                Press(preview, Keys.Home);
                Near(preview.ImageBounds.Y, 0, "Home immediately takes over an active reading scroll");

                // Retargeting by pointer keeps the existing on-screen value instead of snapping.
                preview.ZoomBy(2, center);
                double displayed = preview.ZoomFactor;
                preview.ZoomBy(-1, center);
                Check(preview.IsViewportAnimating && preview.ZoomFactor == displayed,
                    "rapid pointer reversal retains the current displayed transform");
                Press(preview, Keys.W);
            }
            else Console.WriteLine("SKIP: active pointer-to-keyboard takeover needs Windows animations enabled");

            Check(ReferenceEquals(preview.Image, image) && image.Size == new Size(1600, 7200) &&
                image.GetPixel(0, 0).ToArgb() == Color.Coral.ToArgb() &&
                image.GetPixel(image.Width - 1, image.Height - 1).ToArgb() == Color.CornflowerBlue.ToArgb(),
                "keyboard and pointer transforms preserve the original image and endpoint pixels");
            preview.Image = null;
            preview.SynchronizeImage();
            KeyEventArgs empty = SendKey(preview, Keys.Down);
            Check(!empty.Handled && !empty.SuppressKeyPress && !preview.IsViewportAnimating,
                "an empty preview retains native key handling without starting motion");
        }
        finally
        {
            preview.Image = null;
            main.Controls.Remove(preview);
            UiMotion.UserEnabled = originalMotion;
        }
    }
}
