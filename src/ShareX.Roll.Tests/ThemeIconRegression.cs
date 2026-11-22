using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using ShareX.Roll;

internal static class ThemeIconRegression
{
    internal static void Run()
    {
        using Stream source = typeof(ThemeIcons).Assembly.GetManifestResourceStream("SnapFlow.ThemeIcons")!;
        using ZipArchive archive = new(source);
        string[] actions = archive.Entries.Where(entry => entry.FullName.StartsWith("MaterialYou/", StringComparison.Ordinal))
            .Select(entry => Path.GetFileNameWithoutExtension(entry.Name)).Order().ToArray();
        if (actions.Length < 60) throw new InvalidOperationException("The themed icon pack is incomplete.");

        SoftwareStyle[] themes = [SoftwareStyle.Claude, SoftwareStyle.MaterialYou, SoftwareStyle.WinUI, SoftwareStyle.MetroClassic];
        using Bitmap sheet = new(642, 34 + 22 * actions.Length);
        using Graphics preview = Graphics.FromImage(sheet);
        using Font caption = new("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        preview.Clear(Color.White);
        for (int i = 0; i < themes.Length; i++)
            preview.DrawString(themes[i].ToString(), caption, Brushes.Black, 126 + 124 * i, 9);

        for (int row = 0; row < actions.Length; row++)
        {
            string action = actions[row];
            preview.DrawString(action, caption, Brushes.Black, 4, 29 + 22 * row);
            foreach (int dpi in new[] { 96, 120, 144, 192 })
            {
                int size = 16 * dpi / 96;
                using Bitmap sample = new(size + 4, size + 4, PixelFormat.Format32bppArgb);
                using Graphics canvas = Graphics.FromImage(sample);
                if (ThemeIcons.TryDraw(canvas, new RectangleF(2, 2, size, size), SoftwareStyle.Default, action, Color.Black))
                    throw new InvalidOperationException($"Default theme picked a replacement for {action}.");
                for (int i = 0; i < themes.Length; i++)
                {
                    foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
                    {
                        SoftwareStyle theme = themes[i];
                        CapturePalette palette = CapturePalette.For(mode, theme);
                        canvas.Clear(Color.Transparent);
                        if (!ThemeIcons.TryDraw(canvas, new RectangleF(2, 2, size, size), theme, action, palette.Text))
                            throw new InvalidOperationException($"Missing {theme} {action}.");
                        int visible = 0;
                        for (int y = 0; y < sample.Height; y++)
                            for (int x = 0; x < sample.Width; x++)
                            {
                                Color pixel = sample.GetPixel(x, y);
                                if (pixel.A < 128) continue;
                                visible++;
                                if (x < 2 || y < 2 || x >= size + 2 || y >= size + 2)
                                    throw new InvalidOperationException($"Clipped {theme} {action} at {dpi} DPI.");
                                if (Math.Abs(pixel.R - palette.Text.R) > 3 || Math.Abs(pixel.G - palette.Text.G) > 3 ||
                                    Math.Abs(pixel.B - palette.Text.B) > 3)
                                    throw new InvalidOperationException($"Wrong ink for {theme} {action} in {mode} at {dpi} DPI: {pixel}, expected {palette.Text}.");
                            }
                        int minimumPixels = action == "more" ? 3 : 5;
                        if (visible < minimumPixels) throw new InvalidOperationException($"Unreadable {theme} {action} at {dpi} DPI ({visible} pixels).");

                        if (dpi != 96) continue;
                        int left = 126 + 124 * i + (mode == AppearanceMode.Dark ? 52 : 0);
                        int top = 29 + 22 * row;
                        using SolidBrush background = new(palette.Surface);
                        preview.FillRectangle(background, left, top, 51, 21);
                        preview.DrawImageUnscaled(sample, left + 15, top);
                    }
                }
            }
        }
        string folder = Path.Combine("artifacts", "theme-icons");
        Directory.CreateDirectory(folder);
        sheet.Save(Path.Combine(folder, "all-icons.png"), ImageFormat.Png);
        Console.WriteLine($"Theme icons: {actions.Length} actions across four themes, light and dark, 96/120/144/192 DPI.");
    }
}
