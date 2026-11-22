// GPL-3.0-or-later. Style the existing picker without changing its color model or samples.
using System.Drawing.Drawing2D;
using ShareX.HelpersLib;

namespace ShareX.Roll;

internal static class ColorDialogAppearance
{
    private static readonly HashSet<string> FirstColumnUnits = ["lblHue", "lblSaturationPerc", "lblBrightnessPerc"];
    private static readonly HashSet<string> SecondColumnUnits = ["lblCyanPerc", "lblMagentaPerc", "lblYellowPerc", "lblKeyPerc", "cbTransparent"];
    private static readonly HashSet<string> Footer = ["btnOK", "btnCancel", "btnClose", "btnScreenColorPicker", "btnClipboardColorPicker", "btnClipboardStatus", "mbCopy"];

    internal static void Apply(ColorPickerForm dialog, CapturePalette palette)
    {
        float scale = dialog.DeviceDpi / 96f;
        int S(int value) => (int)Math.Round(value * scale);
        dialog.SuspendLayout();
        dialog.MinimizeBox = dialog.MaximizeBox = false;
        Control[] controls = dialog.Controls.Cast<Control>().ToArray();
        foreach (Control control in controls)
        {
            if (Footer.Contains(control.Name)) control.Left += S(48);
            else if (SecondColumnUnits.Contains(control.Name)) control.Left += S(48);
            else if (FirstColumnUnits.Contains(control.Name) || control.Left >= S(480)) control.Left += S(24);
            switch (control)
            {
                case NumericUpDown size:
                    Rectangle bounds = size.Bounds;
                    bounds.Y -= S(4);
                    bounds.Width = S(size.DecimalPlaces > 0 ? 78 : 70);
                    bounds.Height = S(28);
                    DialogChrome.WrapNumber(size, bounds, palette);
                    break;
                case TextBox edit:
                    Rectangle field = edit.Bounds;
                    field.Y -= S(3);
                    field.Height = S(26);
                    DialogChrome.WrapText(edit, field, palette);
                    break;
                case RadioButton radio:
                    StyleRadio(radio, palette);
                    break;
                case ButtonBase button:
                    if (string.IsNullOrEmpty(button.AccessibleName))
                        button.AccessibleName = dialog.ButtonHint(button);
                    DialogChrome.StyleButton(button, palette, button.Name is "btnOK" or "btnClose");
                    if (button is MenuButton { Menu: { } menu }) DialogChrome.StyleMenu(menu, palette, dialog.Font);
                    break;
                case FlowLayoutPanel panel:
                    panel.BackColor = palette.Background;
                    foreach (RadioButton option in panel.Controls.OfType<RadioButton>()) StyleRadio(option, palette);
                    if (panel.Name == "flpColorPalette")
                    {
                        foreach (ColorButton swatch in panel.Controls.OfType<ColorButton>()) StyleSwatch(swatch, palette);
                        panel.ControlAdded += (_, e) => { if (e.Control is ColorButton swatch) StyleSwatch(swatch, palette); };
                    }
                    break;
                case Label label:
                    label.BackColor = palette.Background;
                    label.ForeColor = FirstColumnUnits.Contains(label.Name) || SecondColumnUnits.Contains(label.Name)
                        ? palette.Secondary : palette.Text;
                    break;
                case MyPictureBox preview:
                    preview.PictureBoxBackColor = palette.Surface;
                    break;
                case ColorPicker plane:
                    plane.BackColor = palette.Background;
                    break;
            }
        }
        dialog.ClientSize = new Size(dialog.ClientSize.Width + S(48), dialog.ClientSize.Height);
        // Keep every field reachable on a screen narrower than the scaled dialog.
        Size content = dialog.ClientSize;
        Size frame = dialog.Size - content;
        Size max = Screen.FromControl(dialog).WorkingArea.Size - frame;
        if (content.Width > max.Width || content.Height > max.Height)
        {
            dialog.AutoScroll = true;
            dialog.AutoScrollMinSize = content;
            dialog.ClientSize = new Size(Math.Min(content.Width, max.Width), Math.Min(content.Height, max.Height));
        }
        dialog.ResumeLayout(true);
    }

    private static void StyleSwatch(ColorButton swatch, CapturePalette palette)
    {
        swatch.FlatStyle = FlatStyle.Flat;
        swatch.FlatAppearance.BorderSize = 0;
        swatch.UseVisualStyleBackColor = false;
        float scale = (swatch.Parent?.DeviceDpi ?? swatch.DeviceDpi) / 96f;
        int size = (int)Math.Round(16 * scale);
        swatch.Size = new Size(size, size);
        swatch.Margin = new Padding((int)Math.Round(scale));
        swatch.BorderColor = palette.FieldBorder.IsEmpty ? palette.Secondary : palette.FieldBorder;
        swatch.AccessibleName = "#" + ColorHelpers.ColorToHex(swatch.Color);
        swatch.Paint += (_, e) =>
        {
            // The ColorButton draws the sample on top. Retain its RGB and alpha.
            swatch.BorderColor = swatch.Focused ? palette.Accent : palette.FieldBorder.IsEmpty ? palette.Secondary : palette.FieldBorder;
        };
        swatch.GotFocus += (_, _) => swatch.Invalidate();
        swatch.LostFocus += (_, _) => swatch.Invalidate();
    }

    private static void StyleRadio(RadioButton radio, CapturePalette palette)
    {
        radio.BackColor = palette.Background;
        radio.ForeColor = palette.Text;
        radio.FlatStyle = FlatStyle.Flat;
        radio.UseVisualStyleBackColor = false;
        radio.Cursor = Cursors.Hand;
        bool hovering = false;
        radio.MouseEnter += (_, _) => { hovering = true; radio.Invalidate(); };
        radio.MouseLeave += (_, _) => { hovering = false; radio.Invalidate(); };
        radio.CheckedChanged += (_, _) => radio.Invalidate();
        radio.GotFocus += (_, _) => radio.Invalidate();
        radio.LostFocus += (_, _) => radio.Invalidate();
        radio.ChangeUICues += (_, _) => radio.Invalidate();
        radio.Paint += (_, e) =>
        {
            float s = radio.DeviceDpi / 96f;
            Graphics g = e.Graphics;
            g.Clear(palette.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool rtl = radio.RightToLeft == RightToLeft.Yes;
            RectangleF mark = new(rtl ? radio.Width - 15 * s : s, (radio.Height - 13 * s) / 2, 13 * s, 13 * s);
            Color edge = radio.Checked && radio.Enabled ? palette.Accent :
                palette.FieldBorder.IsEmpty ? palette.Secondary : palette.FieldBorder;
            using SolidBrush face = new(hovering && radio.Enabled ? palette.Hover : palette.Surface);
            using Pen ring = new(edge, s);
            g.FillEllipse(face, mark);
            g.DrawEllipse(ring, mark);
            if (radio.Checked)
            {
                mark.Inflate(-3 * s, -3 * s);
                using SolidBrush dot = new(edge);
                g.FillEllipse(dot, mark);
            }
            int leading = (int)Math.Round(18 * s);
            Rectangle caption = new(rtl ? 0 : leading, 0, Math.Max(0, radio.Width - leading), radio.Height);
            TextRenderer.DrawText(g, radio.Text, radio.Font, caption, radio.Enabled ? palette.Text : palette.Secondary,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                (rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left));
            if (DialogChrome.HasKeyboardFocus(radio))
                CaptureDrawing.DrawInsideBorder(g, radio.ClientRectangle, palette.CornerRadius(3) * s, palette.Accent, s);
        };
    }
}
