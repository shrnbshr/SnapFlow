// GPL-3.0-or-later. Editor-only presentation around the original ShareX option bindings.
using ShareX.HelpersLib;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace ShareX.Roll;

internal static class EditorOptionFields
{
    private static readonly ConditionalWeakTable<ToolStripControlHost, OptionRow> rows = new();

    internal static void Apply(ToolStripControlHost host, CapturePalette palette, bool transparency, int dpi)
    {
        if (host is not ToolStripLabeledNumericUpDown && host is not ToolStripLabeledComboBox) return;
        rows.GetValue(host, item => new OptionRow(item, transparency)).Apply(palette, dpi);
    }

    internal static void ArrangeWidth(ToolStripControlHost host, int width)
    {
        if (rows.TryGetValue(host, out OptionRow? row)) row.ArrangeWidth(width);
    }

    private sealed class OptionRow : Panel
    {
        private readonly ToolStripControlHost host;
        private readonly UserControl content;
        private readonly Label label;
        private readonly Label? suffix;
        private readonly Control field;
        private Rectangle numberClip;
        private bool clipping;
        private int scaleDpi = 96;
        private int S(int value) => (int)Math.Round(value * scaleDpi / 96f);

        internal OptionRow(ToolStripControlHost host, bool transparency)
        {
            this.host = host;
            content = (UserControl)host.Control;
            label = (Label)content.Controls.Find("lblText", true).Single();
            suffix = content.Controls.Find("lblText2", true).OfType<Label>().FirstOrDefault();
            Control original = host is ToolStripLabeledNumericUpDown
                ? content.Controls.Find("nudValue", true).Single()
                : content.Controls.Find("cbList", true).Single();
            // Preserve the original content object and its private fields: ShapeManager updates and
            // reads those exact controls. Only their arrangement and visual wrappers change.
            content.AutoSize = false;
            content.AutoScaleMode = AutoScaleMode.None;
            foreach (Control oldLayout in content.Controls) { oldLayout.AutoSize = false; oldLayout.Visible = false; }
            Name = "editorOptionRow";
            Margin = Padding.Empty;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Anchor = AnchorStyles.None;
            Controls.Add(label);
            label.Visible = true;
            label.TextChanged += (_, _) => PerformLayout();
            if (suffix != null)
            {
                suffix.AutoSize = false;
                suffix.TextAlign = ContentAlignment.MiddleRight;
                suffix.Anchor = AnchorStyles.None;
                Controls.Add(suffix);
                suffix.TextChanged += (_, _) => PerformLayout();
            }
            if (original is NumericUpDown number)
            {
                number.AccessibleName = label.Text;
                field = new SettingsNumberField(number) { Name = "editorNumberField" };
                number.TextAlign = HorizontalAlignment.Center;
                number.Visible = true;
                // Standard ShareX NumericUpDown instances retain all of their original callbacks.
                // Clip their native arrow window while the field supplies the rounded stepper.
                void ClipNumber()
                {
                    if (clipping || number.IsDisposed || !number.IsHandleCreated) return;
                    TextBox? edit = number.Controls.OfType<TextBox>().FirstOrDefault();
                    if (edit == null) return;
                    Rectangle next = Rectangle.Intersect(edit.Bounds, number.ClientRectangle);
                    if (next.IsEmpty || next == numberClip) return;
                    clipping = true;
                    try
                    {
                        numberClip = next;
                        Region? previous = number.Region;
                        number.Region = new Region(next);
                        previous?.Dispose();
                    }
                    finally { clipping = false; }
                }
                number.HandleCreated += (_, _) => { numberClip = Rectangle.Empty; ClipNumber(); };
                number.Layout += (_, _) => ClipNumber();
                foreach (TextBox edit in number.Controls.OfType<TextBox>())
                {
                    edit.SizeChanged += (_, _) => ClipNumber();
                    edit.LocationChanged += (_, _) => ClipNumber();
                }
            }
            else field = new EditorOptionChoice((ComboBox)original, transparency) { AccessibleName = label.Text };
            field.AccessibleName = label.Text;
            Controls.Add(field);
            content.Controls.Add(this);
            Visible = true;
            host.AutoSize = false;
        }

        internal void Apply(CapturePalette palette, int dpi)
        {
            scaleDpi = dpi;
            Font = content.Font = host.Font;
            content.BackColor = BackColor = label.BackColor = palette.Background;
            label.ForeColor = palette.Text;
            if (suffix != null) { suffix.BackColor = palette.Background; suffix.ForeColor = palette.Secondary; }
            if (field is SettingsNumberField number) number.ApplyPalette(palette);
            else ((EditorOptionChoice)field).ApplyPalette(palette);
            int labelWidth = TextRenderer.MeasureText(label.Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            int suffixWidth = suffix == null || suffix.Text.Length == 0 ? 0 :
                TextRenderer.MeasureText(suffix.Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width + S(8);
            int fieldWidth = S(148);
            int width = Math.Max(S(256), labelWidth + S(24) + fieldWidth + suffixWidth);
            int height = Math.Max(S(48), Font.Height + S(24));
            content.Size = host.Size = Size = new(width, height);
            Location = Point.Empty;
            PerformLayout();
        }

        internal void ArrangeWidth(int width)
        {
            content.Width = host.Width = Width = Math.Max(1, width);
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (field == null || label == null) return;
            int suffixWidth = suffix == null || suffix.Text.Length == 0 ? 0 :
                TextRenderer.MeasureText(suffix.Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width + S(8);
            int fieldWidth = S(148);
            field.Bounds = new(Width - suffixWidth - fieldWidth, (Height - S(40)) / 2, fieldWidth, S(40));
            label.Bounds = new(0, 0, Math.Max(1, field.Left - S(16)), Height);
            if (suffix != null)
            {
                suffix.Visible = suffixWidth > 0;
                suffix.Bounds = new(Width - suffixWidth, 0, suffixWidth, Height);
            }
        }
    }
}

internal sealed class EditorOptionChoice : Button
{
    private readonly ComboBox source;
    private readonly bool transparency;
    private CapturePalette palette = CapturePalette.Light;
    private ToolStripDropDown? menu;
    private bool hovering;

    internal EditorOptionChoice(ComboBox source, bool transparency)
    {
        this.source = source;
        this.transparency = transparency;
        Name = "editorOptionChoice";
        AccessibleRole = AccessibleRole.ComboBox;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Margin = Padding.Empty;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        source.SelectedIndexChanged += SourceChanged;
        source.EnabledChanged += SourceChanged;
        SourceChanged(source, EventArgs.Empty);
    }

    private void SourceChanged(object? sender, EventArgs e)
    {
        Text = source.SelectedIndex < 0 ? string.Empty : source.GetItemText(source.SelectedItem);
        Enabled = source.Enabled;
        Invalidate();
    }

    internal void ApplyPalette(CapturePalette value)
    {
        palette = value;
        BackColor = value.Background;
        ForeColor = value.Text;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        Graphics g = e.Graphics;
        g.Clear(palette.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = CaptureDrawing.Rounded(new RectangleF(s, s, Width - 2 * s, Height - 2 * s), 10 * s);
        using SolidBrush fill = new(hovering ? palette.Hover : palette.Canvas);
        g.FillPath(fill, path);
        if (Focused && ShowFocusCues || menu?.Visible == true || SystemInformation.HighContrast)
        {
            using Pen border = new(palette.Accent, 1.5f * s);
            g.DrawPath(border, path);
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(12 * s), 0, Math.Max(1, Width - (int)(42 * s)), Height),
            Enabled ? palette.Text : palette.Secondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | UiLanguage.TextDirection);
        float x = Width - 15 * s, y = Height / 2f;
        using Pen arrow = new(palette.Secondary, 1.5f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(arrow, [new PointF(x - 3 * s, y - 1.5f * s), new(x, y + 1.5f * s), new(x + 3 * s, y - 1.5f * s)]);
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); if (menu?.Visible == true) CloseMenu(); else ShowMenu(); }
    private void ShowMenu()
    {
        if (!Enabled || source.Items.Count == 0) return;
        CloseMenu();
        menu = EditorOverflow.CreateChoices(this, source, palette, transparency);
        menu.Closed += (_, _) => Invalidate();
        menu.Show(this, new Point(0, Height + (int)Math.Round(4 * DeviceDpi / 96f)));
        menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(item => item.Checked)?.Select();
        Invalidate();
    }
    private void CloseMenu() { menu?.Close(); menu?.Dispose(); menu = null; Invalidate(); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.F4 or (Keys.Alt | Keys.Down)) { if (menu?.Visible == true) CloseMenu(); else ShowMenu(); return true; }
        if (keyData == Keys.Escape && menu?.Visible == true) { CloseMenu(); return true; }
        if (Enabled && source.Items.Count > 0 && menu?.Visible != true)
        {
            int next = keyData switch
            {
                Keys.Up => Math.Max(0, source.SelectedIndex - 1),
                Keys.Down => Math.Min(source.Items.Count - 1, source.SelectedIndex + 1),
                Keys.Home => 0,
                Keys.End => source.Items.Count - 1,
                _ => -1
            };
            if (next >= 0) { source.SelectedIndex = next; return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) CloseMenu(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { CloseMenu(); source.SelectedIndexChanged -= SourceChanged; source.EnabledChanged -= SourceChanged; }
        base.Dispose(disposing);
    }
}
