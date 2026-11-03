using ShareX.Roll;
using ShareX.Roll.Localization;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

internal static class PreviewCommandWorkRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private static T Field<T>(object value, string name) =>
        (T)value.GetType().GetField(name, Private)!.GetValue(value)!;

    private static Rectangle[] Geometry(PreviewZoomBar bar) =>
        new[] { bar.Bounds }.Concat(bar.Controls.Cast<Control>().Select(control => control.Bounds)).ToArray();

    private static string[] Semantics(PreviewCanvas preview, PreviewZoomBar bar)
    {
        ToolTip tips = Field<ToolTip>(bar, "tips");
        return new[] { preview.AccessibleName ?? "", preview.AccessibleDescription ?? "",
            bar.AccessibleName ?? "", tips.GetToolTip(bar) ?? "" }
            .Concat(bar.Controls.Cast<Control>().SelectMany(control => new[]
            {
                control.Text, control.AccessibleName ?? "", control.AccessibleDescription ?? "",
                tips.GetToolTip(control) ?? "", control.RightToLeft.ToString(), control.Enabled.ToString()
            })).ToArray();
    }

    private static void VerifyMode(PreviewCanvas preview, PreviewZoomBar bar, bool reading, string context)
    {
        Button minus = Field<Button>(bar, "minus"), plus = Field<Button>(bar, "plus"),
            percent = Field<Button>(bar, "percent"), width = Field<Button>(bar, "width");
        ToolTip tips = Field<ToolTip>(bar, "tips");
        Check(preview.IsWidthReading == reading && preview.AccessibleName == UiText.L("ImagePreview") &&
            preview.AccessibleDescription == UiText.L(reading ? "PreviewReadingHelp" : "PreviewZoomHelp") &&
            bar.AccessibleName == UiText.L("PreviewZoomControls"), context + ": viewport and toolbar accessibility follows mode and language");
        Check(width.Text == UiText.L(reading ? "PreviewFitImage" : "PreviewFitWidth") &&
            width.AccessibleName == width.Text && minus.AccessibleName == UiText.L("PreviewZoomOut") &&
            plus.AccessibleName == UiText.L("PreviewZoomIn") && percent.AccessibleDescription == UiText.L("PreviewFitImage") &&
            percent.AccessibleName == UiText.L("PreviewZoomScale") + " " + percent.Text,
            context + ": fit label, zoom names and percentage accessibility stay current");
        Check(tips.GetToolTip(minus) == UiText.L("PreviewZoomOut") + " (−)" &&
            tips.GetToolTip(plus) == UiText.L("PreviewZoomIn") + " (+)" &&
            tips.GetToolTip(percent) == UiText.L("PreviewFitImage") + " (0)" &&
            tips.GetToolTip(width) == UiText.L(reading ? "PreviewZoomHelp" : "PreviewReadingHelp") &&
            tips.GetToolTip(bar) == UiText.L(reading ? "PreviewReadingHelp" : "PreviewZoomHelp"),
            context + ": every tooltip retains its localized mode-specific meaning");

        Rectangle[] bounds = Geometry(bar);
        string[] semantics = Semantics(preview, bar);
        foreach (CapturePalette palette in new[] { CapturePalette.Dark, CapturePalette.Light })
        {
            bar.ApplyPalette(palette);
            Check(bounds.SequenceEqual(Geometry(bar)) && semantics.SequenceEqual(Semantics(preview, bar)),
                context + ": applying a palette preserves localized accessibility, tooltip state and bounds");
        }
        using Bitmap actual = new(bar.Width, bar.Height);
        bar.DrawToBitmap(actual, bar.ClientRectangle);
        // The former command path performed this full refresh. It must add no missing state.
        preview.ApplyViewportLanguage();
        Check(bounds.SequenceEqual(Geometry(bar)) && semantics.SequenceEqual(Semantics(preview, bar)),
            context + ": explicit full language refresh changes neither bounds nor semantics");
        using Bitmap refreshed = new(bar.Width, bar.Height);
        bar.DrawToBitmap(refreshed, bar.ClientRectangle);
        bool same = true;
        for (int y = 0; y < actual.Height && same; y++)
        for (int x = 0; x < actual.Width; x++)
            if (actual.GetPixel(x, y) != refreshed.GetPixel(x, y)) { same = false; break; }
        Check(same, context + ": toolbar pixels match the explicit full-refresh result");
    }

    private static void VerifyRepeatedWork(PreviewCanvas preview, PreviewZoomBar bar, Action command, string context)
    {
        Rectangle[] before = Geometry(bar);
        int layouts = 0;
        LayoutEventHandler handler = (_, _) => layouts++;
        bar.Layout += handler;
        Stopwatch watch = Stopwatch.StartNew();
        try { for (int i = 0; i < 16; i++) command(); }
        finally { watch.Stop(); bar.Layout -= handler; }
        Console.WriteLine($"PREVIEW-COMMAND {context}: calls=16, layouts={layouts}, totalMs={watch.Elapsed.TotalMilliseconds:F3}; input work only, not screen FPS");
        Check(layouts == 0 && before.SequenceEqual(Geometry(bar)),
            context + ": repeated pointer commands do not lay out the unchanged toolbar");
    }

    internal static Task RunAsync(MainForm main)
    {
        bool previousMotion = UiMotion.UserEnabled;
        LanguageMode previousLanguage = UiLanguage.Options.Single(option => option.CultureName == UiLanguage.Culture.Name).Mode;
        CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo? previousDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        using Bitmap image = new(1600, 7200);
        using (Graphics graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.White);
            for (int y = 0; y < image.Height; y += 160)
                graphics.FillRectangle(y % 320 == 0 ? Brushes.CornflowerBlue : Brushes.Coral, 0, y, image.Width, 80);
        }
        int first = image.GetPixel(0, 0).ToArgb(), last = image.GetPixel(image.Width - 1, image.Height - 1).ToArgb();
        using PreviewCanvas preview = new()
        {
            Location = new Point(-10000, -10000), Size = new Size(720, 460), Image = image,
            SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, ZoomTransparencyEnabled = true
        };
        main.Controls.Add(preview);
        try
        {
            preview.SynchronizeImage();
            PreviewZoomBar bar = (PreviewZoomBar)preview.Controls.Find("previewZoomBar", true).Single();
            PointF anchor = new(preview.Width / 2f, preview.Height / 2f);
            foreach (LanguageMode language in new[] { LanguageMode.SimplifiedChinese, LanguageMode.TraditionalChinese, LanguageMode.English })
            {
                UiMotion.UserEnabled = false;
                UiLanguage.Apply(language);
                preview.ApplyViewportLanguage();
                preview.SetZoomBarHovered(true);
                preview.FitImageWidth(animate: false);
                VerifyMode(preview, bar, true, language + "/width");
                preview.ZoomBy(1, anchor, animate: false);
                VerifyMode(preview, bar, false, language + "/width-to-zoom");
                UiMotion.UserEnabled = true;
                VerifyRepeatedWork(preview, bar, () => preview.ZoomBy(.05, anchor), language + "/repeated-pointer-zoom");
                UiMotion.UserEnabled = false;
                preview.FitImageWidth(animate: false);
                preview.FitImage(animate: false);
                VerifyMode(preview, bar, false, language + "/width-to-fit");
                UiMotion.UserEnabled = true;
                VerifyRepeatedWork(preview, bar, () => preview.FitImage(), language + "/repeated-pointer-fit");
                preview.FitImageWidth(animate: false);
                VerifyRepeatedWork(preview, bar, () => preview.FitImageWidth(), language + "/repeated-pointer-width");
                UiMotion.UserEnabled = false;
                preview.FitImageWidth(animate: false);
            }
            Check(ReferenceEquals(preview.Image, image) && image.Size == new Size(1600, 7200) &&
                image.GetPixel(0, 0).ToArgb() == first && image.GetPixel(image.Width - 1, image.Height - 1).ToArgb() == last,
                "command work optimization retains original image identity, dimensions and endpoint pixels");
            return Task.CompletedTask;
        }
        finally
        {
            preview.Image = null;
            main.Controls.Remove(preview);
            UiLanguage.Apply(previousLanguage);
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefaultUiCulture;
            UiMotion.UserEnabled = previousMotion;
        }
    }
}
