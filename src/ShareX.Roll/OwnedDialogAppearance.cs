// GPL-3.0-or-later. SnapFlow's dialogs inherit the appearance of the editor that owns them.
using System.Runtime.CompilerServices;
using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using ShareX.Roll.Localization;

namespace ShareX.Roll;

internal static class OwnedDialogAppearance
{
    private sealed record Scope(CapturePalette Palette, bool Transparency);
    private static readonly ConditionalWeakTable<Form, Scope> scopes = new();

    static OwnedDialogAppearance() => ShareXResources.OwnedDialogOpening += Prepare;

    internal static void Attach(Form owner, CapturePalette palette, bool transparency) =>
        scopes.Add(owner, new Scope(palette, transparency));

    private static void Prepare(Form dialog, Form owner)
    {
        if (!scopes.TryGetValue(owner, out Scope? scope) || scopes.TryGetValue(dialog, out _)) return;
        scopes.Add(dialog, scope);
        if (dialog is TextDrawingInputBox) return; // Has its own text-preview layout.
        if (dialog is not (ColorPickerForm or ImageInsertForm or StickerForm or StickerPackForm)) return;

        DialogChrome.ApplyFrame(dialog, scope.Palette, scope.Transparency, frameMaterialOnly: dialog is ColorPickerForm);
        dialog.Load += (_, _) =>
        {
            if (dialog is ColorPickerForm picker) ColorDialogAppearance.Apply(picker, scope.Palette);
            else StyleControls(dialog, scope);
        };
    }

    private static void StyleControls(Form dialog, Scope scope)
    {
        CapturePalette palette = scope.Palette;
        bool tile = dialog is ImageInsertForm;
        dialog.SuspendLayout();
        foreach (Control control in dialog.Controls.Cast<Control>().ToArray()) Apply(control);
        if (dialog is StickerForm stickers) stickers.UseSelectionColors(palette.Tint, palette.Hover);
        dialog.ResumeLayout(true);

        void Apply(Control control)
        {
            control.BackColor = palette.Background;
            control.ForeColor = palette.Text;
            switch (control)
            {
                case Button button:
                    DialogChrome.StyleButton(button, palette, button.Name == "btnAdd", tile);
                    break;
                case TextBox edit:
                    DialogChrome.WrapText(edit, edit.Bounds, palette);
                    break;
                case ComboBox combo:
                    EditorOptionChoice choice = new(combo, scope.Transparency)
                    {
                        Bounds = combo.Bounds, Anchor = combo.Anchor, TabIndex = combo.TabIndex,
                        AccessibleName = combo.AccessibleName ?? dialog.Text
                    };
                    combo.Parent!.Controls.Add(choice);
                    combo.Hide();
                    choice.ApplyPalette(palette);
                    break;
                case ToolStrip strip:
                    StyleStickerToolbar(strip, scope);
                    break;
                default:
                    foreach (Control child in control.Controls.Cast<Control>().ToArray()) Apply(child);
                    break;
            }
        }
    }

    private static void StyleStickerToolbar(ToolStrip strip, Scope scope)
    {
        CapturePalette palette = scope.Palette;
        strip.SuspendLayout();
        foreach (ToolStripItem item in strip.Items.Cast<ToolStripItem>().ToArray())
        {
            item.ForeColor = palette.Text;
            Control? field = item switch
            {
                ToolStripTextBox search => DialogChrome.CreateTextField(search.TextBox, palette),
                ToolStripComboBox packs => new EditorOptionChoice(packs.ComboBox, scope.Transparency)
                    { AccessibleName = strip.Items["tslStickers"]?.Text },
                ToolStripNumericUpDown size => new SettingsNumberField(size.NumericUpDownControl),
                _ => null
            };
            if (field == null) continue;
            float scale = strip.DeviceDpi / 96f;
            int index = strip.Items.IndexOf(item);
            int width = Math.Max(item.Width, (int)Math.Round((item is ToolStripComboBox ? 96 : 74) * scale));
            field.Size = new Size(width, (int)Math.Round(28 * scale));
            if (field is EditorOptionChoice choice) choice.ApplyPalette(palette);
            else if (field is SettingsNumberField number) number.ApplyPalette(palette);
            strip.Items.Remove(item);
            strip.Items.Insert(index, new ToolStripControlHost(field)
            {
                Name = item.Name, AutoSize = false, Size = field.Size, Margin = item.Margin
            });
            strip.Disposed += (_, _) => item.Dispose();
        }
        strip.Renderer = new EditorToolStripRenderer(strip, palette);
        strip.ResumeLayout(true);
    }

    internal static DialogResult ConfirmClose(Form owner, CapturePalette palette, bool transparency)
    {
        bool material = palette.IsMaterial;
        if (material) palette = palette with { Background = palette.Surface };
        using Font font = new("Microsoft YaHei UI", 10);
        using Font headingFont = material ? new Font(font.FontFamily, 18) : CaptureTypography.CreateHeading(font, 16, palette);
        using Font? settingsActionFont = palette.HasShapedControls ? null : new Font(font.FontFamily, 11);
        using Form dialog = new();
        dialog.SuspendLayout();
        dialog.Name = "editorCloseConfirmation";
        dialog.Text = "SnapFlow";
        dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
        dialog.MinimizeBox = dialog.MaximizeBox = false;
        dialog.Font = font;
        dialog.AutoScaleMode = AutoScaleMode.Dpi;
        dialog.ClientSize = new Size(416, palette.IsClaude ? 166 : 194);

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(24, material ? 24 : 20, 24, 16), Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Name = "confirmCloseHeading", Text = Strings.EditorCloseHeading, Font = headingFont,
            AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, material ? 12 : 8)
        }, 0, 0);
        Label detail = new DescriptionLabel
        {
            Name = "confirmCloseDetail", Text = Strings.EditorCloseDetail,
            Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = palette.Secondary
        };
        CaptureTypography.ApplyDescription(detail, font, palette);
        layout.Controls.Add(detail, 0, 1);

        TableLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 12, 0, 0)
        };
        if (settingsActionFont != null) actions.Font = settingsActionFont;
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Button AddAction(string label, DialogResult result, int column, int width, int tab)
        {
            Button button = new()
            {
                Name = "confirmClose" + result, Text = label, DialogResult = result,
                Margin = column == 2 ? new Padding(0, 0, 8, 0) : Padding.Empty,
                Size = new Size(width, palette.HasShapedControls ? 40 : ButtonSpacing.ActionHeight), TabIndex = tab
            };
            actions.Controls.Add(button, column, 0);
            bool primary = result == DialogResult.Yes;
            if (palette.HasShapedControls)
            {
                DialogChrome.StyleButton(button, palette, primary, quiet: result == DialogResult.No);
            }
            else
            {
                if (!primary) button.Tag = "quiet";
                button.AutoSize = true;
                button.Padding = ButtonSpacing.ActionPadding;
                palette.StyleButton(button, primary);
            }
            return button;
        }
        AddAction(Strings.EditorCloseDiscard, DialogResult.No, column: 0, width: 96, tab: 2);
        dialog.CancelButton = AddAction(Strings.Cancel, DialogResult.Cancel, column: 2, width: 80, tab: 1);
        dialog.AcceptButton = AddAction(Strings.Save, DialogResult.Yes, column: 3, width: 88, tab: 0);
        layout.Controls.Add(actions, 0, 2);
        dialog.Controls.Add(layout);
        UiLanguage.ApplyTextDirection(dialog);
        DialogChrome.ApplyFrame(dialog, palette, transparency, frameMaterialOnly: true);
        // Scale the form and its complete layout from the same logical baseline.
        dialog.AutoScaleDimensions = new SizeF(96, 96);
        dialog.ResumeLayout(true);
        return dialog.ShowDialogTopMost(owner);
    }
}
