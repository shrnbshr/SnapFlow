using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

internal static class EditorTextDialogRegression
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private static T Find<T>(Form dialog, string name) where T : Control =>
        dialog.Controls.Find(name, true).OfType<T>().Single();

    private static T Field<T>(Form dialog, string name) where T : class =>
        (T)dialog.GetType().GetField(name, Members)!.GetValue(dialog)!;

    private static bool SameColor(Color first, Color second) => first.ToArgb() == second.ToArgb();

    private static bool IsSurface(Color value, CapturePalette palette) =>
        SameColor(value, palette.Surface) || SameColor(value, palette.Canvas) || SameColor(value, palette.Background);

    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            double channel = value / 255.0;
            return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }

        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }

    internal static async Task RunAsync(MainForm main)
    {
        Directory.CreateDirectory("artifacts/image-editor");
        try
        {
            foreach ((string name, CapturePalette palette, bool dark) in new[]
            {
                ("default-light", CapturePalette.Light, false), ("default-dark", CapturePalette.Dark, true),
                ("claude-light", CapturePalette.ClaudeLight, false), ("claude-dark", CapturePalette.ClaudeDark, true)
            })
            {
                palette.ApplyLibraryTheme();
                using Bitmap source = new(600, 400);
                using RegionCaptureForm editor = ScreenshotEditor.Create(source, palette);
                editor.TopMost = true;
                editor.Show(main);
                try
                {
                    editor.ForceActivate();
                    if (editor.IsEditorCropPending) editor.CancelEditorCrop();
                    object manager = editor.GetType().GetProperty("ShapeManager", Members)!.GetValue(editor)!;
                    Type managerType = manager.GetType();
                    TextDrawingShape shape = (TextDrawingShape)managerType.GetMethod("CreateShape", Members, null, [typeof(ShapeType)], null)!
                        .Invoke(manager, [ShapeType.DrawingTextOutline])!;
                    managerType.GetMethod("AddShape", Members, null, [typeof(BaseShape)], null)!.Invoke(manager, [shape]);
                    Color annotation = dark ? Color.Black : Color.White;
                    shape.TextOptions.Color = annotation;
                    shape.TextOptions.Bold = false;
                    shape.TextOptions.Size = 18;
                    const string sample = "文字标注\r\nSnapFlow";

                    await OpenModalAsync(shape.OnCreating, async dialog =>
                    {
                        string context = name + " @ " + dialog.DeviceDpi + " DPI";
                        TextBox input = Find<TextBox>(dialog, "txtInput");
                        Check(dialog.Owner == editor && dialog.Text.StartsWith("SnapFlow", StringComparison.Ordinal) &&
                            (dialog.ActiveControl == input || input.Focused), context + ": editor owns the modal and the text field has initial focus");
                        Check(SameColor(dialog.BackColor, palette.Background) && SameColor(dialog.ForeColor, palette.Text),
                            context + ": dialog has the editor's background and text colors");
                        Label tip = Find<Label>(dialog, "lblTip");
                        using Font expectedTipFont = CaptureTypography.CreateDescription(dialog.Font, tip.Font.SizeInPoints, palette);
                        Check(SameColor(tip.ForeColor, palette.Secondary) && tip.Font.Name == expectedTipFont.Name,
                            context + ": key hint follows the theme's secondary text and typography");
                        ComboBox fonts = Find<ComboBox>(dialog, "cbFonts");
                        NumericUpDown size = Find<NumericUpDown>(dialog, "nudTextSize");
                        Check(fonts.Items.Count > 0 && IsSurface(fonts.BackColor, palette) && IsSurface(size.BackColor, palette) &&
                            SameColor(fonts.ForeColor, palette.Text) && SameColor(size.ForeColor, palette.Text),
                            context + ": font and size controls use themed surfaces and legible text");
                        int originalFont = fonts.SelectedIndex;
                        fonts.Focus();
                        fonts.DroppedDown = true;
                        Check(fonts.DroppedDown, context + ": the font picker retains its native dropdown");
                        int fontIndex = fonts.Items.IndexOf("Segoe UI");
                        if (fontIndex >= 0)
                        {
                            fonts.SelectedIndex = fontIndex;
                            Check(input.Font.Name == "Segoe UI", context + ": the font picker updates the native preview");
                        }
                        fonts.DroppedDown = false;
                        fonts.SelectedIndex = originalFont;
                        Control tools = Find<Control>(dialog, "flpProperties");
                        Check(tools.Controls.Cast<Control>().Where(control => control.Visible)
                            .All(control => tools.ClientRectangle.Contains(control.Bounds)), context + ": toolbar keeps all visible controls within its bounds");
                        Button ok = Find<Button>(dialog, "btnOK");
                        Button cancel = Find<Button>(dialog, "btnCancel");
                        Rectangle textBounds = ClientBounds(dialog, input);
                        Rectangle tipBounds = ClientBounds(dialog, tip);
                        Check(textBounds.Top >= ClientBounds(dialog, tools).Bottom && textBounds.Bottom <= ClientBounds(dialog, ok).Top &&
                            !tipBounds.IntersectsWith(ClientBounds(dialog, ok)) && !tipBounds.IntersectsWith(ClientBounds(dialog, cancel)),
                            context + ": toolbar, preview, key hint and actions fit without overlap");

                        double contrast = (Math.Max(Luminance(annotation), Luminance(input.BackColor)) + .05) /
                            (Math.Min(Luminance(annotation), Luminance(input.BackColor)) + .05);
                        Check(SameColor(input.ForeColor, annotation) && contrast >= 4.5,
                            context + ": alternate preview surface keeps the selected annotation color legible");
                        Find<ColorButton>(dialog, "btnTextColor").Color = palette.Text;
                        CheckBox bold = Find<CheckBox>(dialog, "cbBold");
                        bold.Checked = true;
                        size.Value = 24;
                        bool BarVisible() => (NativeMethods.GetWindowLong(input.Handle, NativeConstants.GWL_STYLE).ToInt64() & (long)WindowStyles.WS_VSCROLL) != 0;
                        input.Text = string.Join("\r\n", Enumerable.Repeat("SnapFlow", 30));
                        Check(BarVisible(), context + ": the native scrollbar is available for long text");
                        input.Text = sample;
                        Check(!BarVisible(), context + ": short text has no empty scrollbar");
                        Check(IsSurface(input.BackColor, palette) && SameColor(input.ForeColor, palette.Text) &&
                            input.Font.Bold && input.Font.SizeInPoints == 24, context + ": themed surface and annotation formatting survive updates");
                        using (Bitmap selected = new(bold.Width, bold.Height))
                        {
                            bold.DrawToBitmap(selected, bold.ClientRectangle);
                            Check(SameColor(selected.GetPixel(bold.Width / 2, bold.Height / 5), palette.Tint),
                                context + ": bold button uses the themed selected fill");
                        }
                        foreach ((string menuName, string buttonName) in new[]
                        {
                            ("cmsAlignmentHorizontal", "btnAlignmentHorizontal"),
                            ("cmsAlignmentVertical", "btnAlignmentVertical"),
                            ("cmsGradient", "btnGradient")
                        })
                        {
                            ContextMenuStrip menu = Field<ContextMenuStrip>(dialog, menuName);
                            Button button = Find<Button>(dialog, buttonName);
                            button.PerformClick();
                            try
                            {
                                Check(menu.Visible && menu.RenderMode == ToolStripRenderMode.Custom && IsSurface(menu.BackColor, palette),
                                    context + ": " + menuName + " opens with the editor's palette");
                            }
                            finally { menu.Close(); }
                        }
                        input.Focus();
                        input.SelectionStart = input.TextLength;
                        input.SelectionLength = 0;
                        await Task.Delay(50);
                        if (dialog.DeviceDpi == 96)
                            EditorRegression.Capture(dialog, "text-dialog-" + name);
                        else
                        {
                            using Bitmap image = new(dialog.Width, dialog.Height);
                            using Graphics graphics = Graphics.FromImage(image);
                            graphics.CopyFromScreen(dialog.Location, Point.Empty, dialog.Size);
                            image.Save($"artifacts/image-editor/text-dialog-{name}-{dialog.DeviceDpi}.png", ImageFormat.Png);
                        }
                        ok.PerformClick();
                    });
                    Check(shape.Text == sample && shape.TextOptions.Bold && shape.TextOptions.Size == 24 &&
                        SameColor(shape.TextOptions.Color, palette.Text), name + ": OK creates the annotation with its formatting");

                    await OpenModalAsync(shape.OnDoubleClicked, dialog =>
                    {
                        TextBox input = Find<TextBox>(dialog, "txtInput");
                        Check(input.Text == sample && SameColor(dialog.BackColor, palette.Background),
                            name + ": editing an existing annotation opens the same themed modal");
                        input.Text = "未保存的修改";
                        Find<Button>(dialog, "btnCancel").PerformClick();
                        return Task.CompletedTask;
                    });
                    Check(shape.Text == sample, name + ": Cancel preserves the saved annotation text");
                }
                finally { editor.GetType().GetMethod("CloseWindow", Members)!.Invoke(editor, [RegionResult.Close]); }
            }
        }
        finally { CapturePalette.Current.ApplyLibraryTheme(); }
    }

    private static Rectangle ClientBounds(Form dialog, Control control) =>
        dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));

    private static async Task OpenModalAsync(Action open, Func<Form, Task> inspect)
    {
        Task driver = InspectAsync();
        open();
        await driver;

        async Task InspectAsync()
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                await Task.Delay(25);
                Form? dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Name == "TextDrawingInputBox" && form.Visible);
                if (dialog == null) continue;
                try { await inspect(dialog); }
                finally { if (dialog.Visible && dialog.DialogResult == DialogResult.None) dialog.DialogResult = DialogResult.Cancel; }
                return;
            }
            throw new TimeoutException("The editor text dialog did not open");
        }
    }
}
