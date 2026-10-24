using ShareX.Roll;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

internal static class SaveFolderPresentationRegression
{
    private static T Find<T>(Control control, string name) where T : Control => (T)control.Controls.Find(name, true).Single();
    private static FieldInfo Member(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static T Field<T>(object target, string name) => (T)Member(target, name).GetValue(target)!;
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target,args);
    private static RectangleF Thumb(Control bar) => (RectangleF)bar.GetType().GetProperty("Thumb",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bar)!;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS: " + message); }
    private static Bitmap Render(Control control) { Bitmap image = new(control.Width,control.Height); control.DrawToBitmap(image,control.ClientRectangle); return image; }

    internal static async Task RunAsync(MainForm main, bool baseline)
    {
        string output = Path.GetFullPath("artifacts/save-folder-presentation/" + (baseline ? "before" : "after"));
        Directory.CreateDirectory(output);
        ApplicationPreferences original = Field<ApplicationPreferences>(main,"preferences");
        Size oldSize = main.Size;
        bool oldMotion = UiMotion.UserEnabled;
        try
        {
            foreach (LanguageMode language in baseline ? new[] { LanguageMode.SimplifiedChinese } : new[] { LanguageMode.SimplifiedChinese,LanguageMode.TraditionalChinese,LanguageMode.English })
            foreach (AppearanceMode mode in baseline ? new[] { AppearanceMode.Light } : new[] { AppearanceMode.Light,AppearanceMode.Dark })
            foreach (bool compact in baseline ? new[] { false } : new[] { false,true })
            {
                ApplicationPreferences preferences = original.Clone();
                preferences.Language = language; preferences.Appearance = mode; preferences.AnimationsEnabled = true;
                preferences.UseDefaultSaveFolder = false; preferences.DefaultSaveFolder = "";
                foreach (string key in preferences.Shortcuts.Keys.ToArray()) preferences.Shortcuts[key] = Keys.None;
                Member(main,"preferences").SetValue(main,preferences);
                UiLanguage.Apply(language); Call(main,"ApplyAppearance");
                main.Size = compact ? main.MinimumSize : oldSize;
                Call(main,"OpenSettings"); await Task.Delay(300);
                SettingsPage page = Find<SettingsPage>(main,"SettingsPage");
                Panel viewport = Field<Panel>(page,"scroller");
                Control group = Find<Control>(page,"saveLocationGroup");
                Control field = Find<Control>(page,"defaultSaveFolderField");
                Button browse = Find<Button>(page,"browseSaveFolderButton");
                CheckBox toggle = Find<CheckBox>(page,"defaultSaveFolderToggle");
                Control bar = Find<Control>(page,"settingsScrollBar");
                viewport.ScrollControlIntoView(group);
                toggle.Checked = true;
                await Task.Delay(350);
                Console.WriteLine($"METRIC: {language}/{mode}/{compact} dpi={main.DeviceDpi} field={field.Size} preferred={field.GetPreferredSize(Size.Empty)} browse={browse.Size}");
                using (Bitmap image = Render(group)) image.Save(Path.Combine(output,$"empty-{language}-{mode}-{(compact ? "compact" : "regular")}-{main.DeviceDpi}.png"),ImageFormat.Png);
                if (!baseline)
                {
                    Check(field.Height <= Math.Max(browse.Height,44 * main.DeviceDpi / 96), "initially empty path field stays one compact row");
                    Check(!Find<Label>(page,"saveFolderValidation").Visible, "empty state does not repeat the folder prompt below the field");
                    Check(!viewport.HorizontalScroll.Visible, "empty selection fits the minimum and regular widths");
                    using Bitmap image = Render(field);
                    int ink = 0;
                    CapturePalette palette = CapturePalette.For(mode);
                    for (int y = 5 * main.DeviceDpi / 96; y < image.Height-5 * main.DeviceDpi / 96; y++)
                    for (int x = 38 * main.DeviceDpi / 96; x < image.Width-16 * main.DeviceDpi / 96; x++)
                    {
                        Color color = image.GetPixel(x,y);
                        if (Math.Abs(color.R-palette.Canvas.R) + Math.Abs(color.G-palette.Canvas.G) + Math.Abs(color.B-palette.Canvas.B) > 90) ink++;
                    }
                    Check(ink > 40, "the empty field actually paints a visible placeholder");
                    if (language == LanguageMode.SimplifiedChinese && !compact) await VerifyThumb(page,toggle,bar,output,mode,main.DeviceDpi);
                }
                Call(main,"CloseSettings"); await Task.Delay(230);
            }
        }
        finally
        {
            Call(main,"CloseSettings"); Member(main,"preferences").SetValue(main,original);
            main.Size = oldSize; UiLanguage.Apply(original.Language); Call(main,"ApplyAppearance"); UiMotion.UserEnabled = oldMotion;
        }
    }

    private static async Task VerifyThumb(SettingsPage page, CheckBox toggle, Control bar, string output, AppearanceMode mode, int dpi)
    {
        Panel viewport = Field<Panel>(page,"scroller");
        Control content = Field<Control>(page,"content");
        viewport.AutoScrollPosition = Point.Empty;
        await Task.Delay(240);
        RectangleF expanded = Thumb(bar);
        toggle.Checked = false;
        RectangleF first = Thumb(bar);
        int layouts = 0;
        LayoutEventHandler count = (_,_) => layouts++;
        content.Layout += count;
        await Task.Delay(60);
        RectangleF middle = Thumb(bar);
        using (Bitmap image = Render(bar)) image.Save(Path.Combine(output,$"scrollbar-middle-{mode}-{dpi}.png"),ImageFormat.Png);
        await Task.Delay(260);
        content.Layout -= count;
        RectangleF collapsed = Thumb(bar);
        if (UiMotion.Enabled)
        {
            Check(Math.Abs(first.Height-expanded.Height)<.5, mode + ": scrollbar resizing begins at its current visible length");
            Check(middle.Height>expanded.Height && middle.Height<collapsed.Height, mode + ": scrollbar has real intermediate lengths");
            Check(layouts==0, mode + ": resizing the thumb does not re-layout the settings content each frame");
        }
        Check(!Field<UiTween>(bar,"geometry").IsRunning, mode + ": the geometry timer stops at the exact final length");
        toggle.Checked = true; await Task.Delay(50);
        RectangleF before = Thumb(bar);
        toggle.Checked = false;
        Check(Math.Abs(Thumb(bar).Height-before.Height)<.5, mode + ": rapid reversal preserves the current visible length");
        await Task.Delay(250);
        bool enabled = UiMotion.UserEnabled; UiMotion.UserEnabled = false;
        toggle.Checked = true;
        Check(!Field<UiTween>(bar,"geometry").IsRunning && Thumb(bar).Height<collapsed.Height, mode + ": reduced motion updates scrollbar geometry immediately");
        UiMotion.UserEnabled = enabled;
        toggle.Checked = false;
        Call(bar,"OnKeyDown",new KeyEventArgs(Keys.End));
        Check(-viewport.AutoScrollPosition.Y==viewport.DisplayRectangle.Height-viewport.ClientSize.Height && !Field<UiTween>(bar,"geometry").IsRunning,
            mode + ": keyboard navigation interrupts resizing and uses the native scroll range");
        Call(bar,"OnKeyDown",new KeyEventArgs(Keys.Home));
        toggle.Checked = true; await Task.Delay(250);
    }
}
