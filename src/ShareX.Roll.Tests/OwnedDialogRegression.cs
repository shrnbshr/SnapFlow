using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.Roll.Localization;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

internal static class OwnedDialogRegression
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static async Task RunAsync(MainForm main)
    {
        Directory.CreateDirectory("artifacts/image-editor");
        try
        {
            foreach ((string style, CapturePalette palette) in new[]
            {
                ("default-light", CapturePalette.Light), ("default-dark", CapturePalette.Dark),
                ("claude-light", CapturePalette.ClaudeLight), ("claude-dark", CapturePalette.ClaudeDark)
            })
            {
                palette.ApplyLibraryTheme();
                using Bitmap source = new(480, 320);
                using RegionCaptureForm editor = ScreenshotEditor.Create(source, palette);
                editor.TopMost = true;
                editor.Show(main);
                try
                {
                    if (editor.IsEditorCropPending) editor.CancelEditorCrop();
                    object manager = editor.GetType().GetProperty("ShapeManager", Members)!.GetValue(editor)!;
                    Type type = manager.GetType();
                    type.GetProperty("CurrentTool")!.SetValue(manager, ShapeType.DrawingRectangle);
                    string suffix = style + "-" + editor.DeviceDpi;
                    await InModal<ColorPickerForm>(editor.ChooseEditorAnnotationColor, async picker =>
                    {
                        CheckAppearance(picker, editor, palette, "color picker");
                        NumericUpDown red = Find<NumericUpDown>(picker, "nudRed");
                        NumericUpDown alpha = Find<NumericUpDown>(picker, "nudAlpha");
                        TextBox hex = Find<TextBox>(picker, "txtHex");
                        TextBox dec = Find<TextBox>(picker, "txtDecimal");
                        Check(red.Parent is SettingsNumberField && alpha.Parent is SettingsNumberField &&
                            red.BackColor == palette.Canvas && hex.BackColor == palette.Canvas && dec.ForeColor == palette.Text,
                            suffix + ": color fields use the dialog palette");
                        RadioButton standards = Find<RadioButton>(picker, "rbStandardColors");
                        standards.Checked = true;
                        FlowLayoutPanel samples = Find<FlowLayoutPanel>(picker, "flpColorPalette");
                        ColorButton swatch = samples.Controls.OfType<ColorButton>().First();
                        swatch.PerformClick();
                        Check(samples.Controls.Count > 8 && picker.NewColor.RGBA.Red == swatch.Color.R && swatch.AccessibleName!.StartsWith('#'),
                            suffix + ": toggling to standard colors themes and activates new swatches");
                        picker.SetCurrentColor(Color.White, true);
                        alpha.Value = 128;
                        Check(picker.NewColor.RGBA.Alpha == 128, suffix + ": entering alpha updates the original model");
                        Find<ColorButton>(picker, "cbTransparent").PerformClick();
                        Check(picker.NewColor.RGBA.Alpha == 0 && Application.OpenForms.OfType<ColorPickerForm>().Count() == 1,
                            suffix + ": the transparency control stays in this picker");
                        picker.SetCurrentColor(Color.Teal, true);
                        foreach (string name in new[] { "nudGreen", "nudAlpha", "nudCyan" })
                        {
                            TextBox edit = Find<NumericUpDown>(picker, name).Controls.OfType<TextBox>().Single();
                            int width = TextRenderer.MeasureText(edit.Text, edit.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                            Check(edit.Width >= width, suffix + ": " + name + " can display its entire value");
                        }
                        Check(radiosAndFieldsFit(picker), suffix + ": picker fields and actions fit inside the client area");
                        await Capture(picker, "color-picker-" + suffix);
                        Find<Button>(picker, "btnOK").PerformClick();
                    });
                    Check(editor.EditorAnnotationColor.ToArgb() == Color.Teal.ToArgb(), suffix + ": picking commits the annotation color");

                    BaseShape MakeShape(ShapeType shape) => (BaseShape)type.GetMethod("CreateShape", Members, null, [typeof(ShapeType)], null)!
                        .Invoke(manager, [shape])!;
                    TextDrawingShape text = (TextDrawingShape)MakeShape(ShapeType.DrawingTextOutline);
                    await ModalNamed("TextDrawingInputBox", text.OnDoubleClicked, async dialog =>
                    {
                        await InModal<ColorPickerForm>(Find<ColorButton>(dialog, "btnTextColor").PerformClick, picker =>
                        {
                            CheckAppearance(picker, dialog, palette, "nested text color picker");
                            Find<Button>(picker, "btnCancel").PerformClick();
                            return Task.CompletedTask;
                        });
                        Find<Button>(dialog, "btnCancel").PerformClick();
                    });

                    using Bitmap inserted = new(16, 12);
                    await InModal<ImageInsertForm>(() => type.GetMethod("InsertImage", Members)!.Invoke(manager, [inserted]), async dialog =>
                    {
                        CheckAppearance(dialog, editor, palette, "image insert dialog");
                        Check(dialog.Controls.OfType<Button>().Count(button => button.FlatStyle == FlatStyle.Flat) == 3, suffix + ": three insert actions are themed");
                        await Capture(dialog, "image-insert-" + suffix);
                        dialog.Close();
                    });

                    BaseShape sticker = MakeShape(ShapeType.DrawingSticker);
                    await InModal<StickerForm>(sticker.OnDoubleClicked, async dialog =>
                    {
                        CheckAppearance(dialog, editor, palette, "sticker browser");
                        ToolStrip bar = Find<ToolStrip>(dialog, "tsMain");
                        Check(bar.Renderer is EditorToolStripRenderer && bar.Items["tsnudSize"] is ToolStripControlHost { Control: SettingsNumberField },
                            suffix + ": sticker toolbar uses the editor palette and numeric control");
                        await Capture(dialog, "stickers-" + suffix);
                        await InModal<StickerPackForm>(bar.Items["tsbEditStickers"]!.PerformClick, async packs =>
                        {
                            CheckAppearance(packs, dialog, palette, "sticker pack settings");
                            Check(Find<TextBox>(packs, "txtFolder").BackColor == palette.Canvas, suffix + ": pack inputs use the themed face");
                            await Capture(packs, "sticker-packs-" + suffix);
                            packs.Close();
                        });
                        dialog.Close();
                    });

                    PropertyInfo modified = type.GetProperty("IsImageModified", Members)!;
                    modified.SetValue(manager, true);
                    bool mayClose = true;
                    await ModalNamed("editorCloseConfirmation",
                        () => mayClose = (bool)editor.GetType().GetMethod("ShowExitConfirmation", Members)!.Invoke(editor, null)!, async dialog =>
                    {
                        CheckAppearance(dialog, editor, palette, "close confirmation");
                        Check(dialog.AcceptButton is Button && dialog.CancelButton is Button, suffix + ": close prompt supports Enter and Escape");
                        await Capture(dialog, "editor-close-" + suffix);
                        Find<Button>(dialog, "confirmCloseCancel").PerformClick();
                    });
                    Check(!mayClose, suffix + ": canceling the close prompt keeps the editor open");
                    modified.SetValue(manager, false);
                }
                finally { editor.GetType().GetMethod("CloseWindow", Members)!.Invoke(editor, [RegionResult.Close]); }
            }
        }
        finally { CapturePalette.Current.ApplyLibraryTheme(); }
    }

    private static bool radiosAndFieldsFit(Form dialog)
    {
        Rectangle client = dialog.RectangleToScreen(dialog.ClientRectangle);
        return dialog.Controls.Cast<Control>().Where(control => control.Visible)
            .All(control => client.Contains(control.Parent!.RectangleToScreen(control.Bounds)));
    }

    internal static async Task VerifyCloseAsync(MainForm main)
    {
        Directory.CreateDirectory("artifacts/image-editor");
        LanguageMode originalLanguage = UiLanguage.Options.FirstOrDefault(option => option.CultureName == UiLanguage.Culture.Name)?.Mode ?? LanguageMode.System;
        try
        {
            foreach ((string style, CapturePalette palette) in new[]
            {
                ("default-light", CapturePalette.Light), ("default-dark", CapturePalette.Dark),
                ("claude-light", CapturePalette.ClaudeLight), ("claude-dark", CapturePalette.ClaudeDark)
            })
            foreach (LanguageMode language in new[] { LanguageMode.SimplifiedChinese, LanguageMode.TraditionalChinese, LanguageMode.English })
            {
                UiLanguage.Apply(language);
                DialogResult result = DialogResult.None;
                string context = style + "/" + UiLanguage.Culture.Name;
                await ModalNamed("editorCloseConfirmation",
                    () => result = OwnedDialogAppearance.ConfirmClose(main, palette, palette.AllowsTransparency(true)),
                    async dialog =>
                    {
                        float scale = dialog.DeviceDpi / 96f;
                        CheckAppearance(dialog, main, palette, "redesigned close confirmation");
                        Label heading = Find<Label>(dialog, "confirmCloseHeading");
                        Label detail = Find<Label>(dialog, "confirmCloseDetail");
                        Button save = Find<Button>(dialog, "confirmCloseYes");
                        Button cancel = Find<Button>(dialog, "confirmCloseCancel");
                        Button discard = Find<Button>(dialog, "confirmCloseNo");
                        Check(dialog.ClientSize.Width >= 410 * scale && save.Height >= 38 * scale,
                            context + ": client and action dimensions scale with the display");
                        Size caption = TextRenderer.MeasureText(detail.Text, detail.Font, new Size(detail.Width, 0),
                            TextFormatFlags.WordBreak);
                        Check(heading.Text == Strings.EditorCloseHeading && heading.Font.Bold &&
                            detail.Text == Strings.EditorCloseDetail && detail.Height >= caption.Height &&
                            detail.ForeColor == palette.Secondary, $"{context}: both levels of text fit ({detail.Height}px available, {caption.Height}px required)");
                        Rectangle client = dialog.RectangleToScreen(dialog.ClientRectangle);
                        Rectangle VisibleBounds(Control control) => control.RectangleToScreen(control.ClientRectangle);
                        Check(new Control[] { heading, detail, discard, cancel, save }.All(control => client.Contains(VisibleBounds(control))) &&
                            VisibleBounds(detail).Bottom <= VisibleBounds(save).Top &&
                            VisibleBounds(discard).Right < VisibleBounds(cancel).Left &&
                            VisibleBounds(cancel).Right < VisibleBounds(save).Left, context + ": all content fits without overlap");
                        Check(save.Text == Strings.Save && cancel.Text == Strings.Cancel && discard.Text == Strings.EditorCloseDiscard &&
                            dialog.AcceptButton == save && dialog.CancelButton == cancel, context + ": actions have explicit labels and keyboard defaults");
                        if (!palette.IsClaude)
                        {
                            Check(new[] { save, cancel, discard }.All(CaptureButtonPainter.HasBackdrop) &&
                                Equals(cancel.Tag, "quiet") && Equals(discard.Tag, "quiet") &&
                                save.BackColor == palette.Accent && save.Height >= ButtonSpacing.ActionHeight * scale,
                                context + ": the dialog uses Settings button styling and feedback");
                        }
                        if (language == LanguageMode.SimplifiedChinese)
                        {
                            await Task.Delay(200); // Let the native opening transition settle before the screen capture.
                            await Capture(dialog, "close-confirmation-" + style + "-" + dialog.DeviceDpi);
                        }
                        (language switch
                        {
                            LanguageMode.English => save,
                            LanguageMode.TraditionalChinese => discard,
                            _ => cancel
                        }).PerformClick();
                    });
                Check(result == (language switch
                {
                    LanguageMode.English => DialogResult.Yes,
                    LanguageMode.TraditionalChinese => DialogResult.No,
                    _ => DialogResult.Cancel
                }), context + ": clicking the action returns its original result");
            }
        }
        finally { UiLanguage.Apply(originalLanguage); }
    }

    private static T Find<T>(Control dialog, string name) where T : Control =>
        dialog.Controls.Find(name, true).OfType<T>().Single();

    private static void CheckAppearance(Form dialog, Form owner, CapturePalette palette, string name)
    {
        Check(dialog.Owner == owner && dialog.BackColor == palette.Background && dialog.ForeColor == palette.Text &&
            !dialog.Text.StartsWith("ShareX", StringComparison.Ordinal), $"{name} follows its owner at {dialog.DeviceDpi} DPI");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private static async Task Capture(Form dialog, string name)
    {
        await Task.Delay(55);
        using Bitmap image = new(dialog.Width, dialog.Height);
        if (dialog.DeviceDpi == 96) dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size));
        else
        {
            using Graphics graphics = Graphics.FromImage(image);
            graphics.CopyFromScreen(dialog.Location, Point.Empty, dialog.Size);
        }
        image.Save(Path.Combine("artifacts/image-editor", name + ".png"), ImageFormat.Png);
    }

    private static Task InModal<T>(Action open, Func<T, Task> inspect) where T : Form =>
        ModalNamed(typeof(T).Name, open, dialog => inspect((T)dialog));

    private static async Task ModalNamed(string name, Action open, Func<Form, Task> inspect)
    {
        Task driver = InspectAsync();
        open();
        await driver;

        async Task InspectAsync()
        {
            for (int i = 0; i < 200; i++)
            {
                await Task.Delay(25);
                Form? dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Visible && form.Name == name);
                if (dialog == null) continue;
                try { await inspect(dialog); }
                finally { if (dialog.Visible) dialog.Close(); }
                return;
            }
            throw new TimeoutException("Window did not open: " + name);
        }
    }
}
