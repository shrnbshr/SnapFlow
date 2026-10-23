// GPL-3.0-or-later. Inline settings UI; the capture engine and its option ranges are unchanged.
using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using static ShareX.Roll.Localization.UiText;

namespace ShareX.Roll;

internal sealed class ScrollingOptionsEditor : TableLayoutPanel
{
    private readonly NumericUpDown startDelay = Number("nudStartDelay", 100, 5000, 100);
    private readonly NumericUpDown scrollDelay = Number("nudScrollDelay", 100, 5000, 100);
    private readonly NumericUpDown scrollAmount = Number("nudScrollAmount", 1, 6, 1);
    private readonly SettingsChoice method = new() { Name = "cbScrollMethod", Width = 184, DropDownWidth = 260, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
    private readonly MotionToggle autoTop = Toggle("cbAutoScrollTop");
    private readonly MotionToggle showRegion = Toggle("cbShowRegion");
    private readonly MotionToggle ignoreBottom = Toggle("cbAutoIgnoreBottomEdge");
    private readonly MotionToggle autoSave = Toggle("cbAutoUpload");
    private readonly Control amountRow;
    private readonly ComponentResourceManager resources = new(typeof(ScrollingCaptureOptionsForm));
    internal event EventHandler? Changed;

    internal ScrollingOptionsEditor(ScrollingCaptureOptions options)
    {
        Name = "scrollingOptionsEditor";
        Dock = DockStyle.Top;
        AutoSize = true;
        ColumnCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Padding = new Padding(0, 8, 0, 12);
        Margin = Padding.Empty;
        method.Items.AddRange(Helpers.GetLocalizedEnumDescriptions<ScrollMethod>());
        AddRow("lblStartDelay", WithUnit(startDelay, "ms"));
        AddRow("lblScrollDelay", WithUnit(scrollDelay, "ms"));
        AddRow("lblScrollMethod", method);
        amountRow = AddRow("lblScrollAmount", WithUnit(scrollAmount, TextFor("lblScrollAmountHint")));
        AddRow("cbAutoScrollTop", autoTop);
        AddRow("cbShowRegion", showRegion);
        AddRow("cbAutoIgnoreBottomEdge", ignoreBottom);
        AddRow("cbAutoUpload", autoSave, L("ScrollAutoSave"));
        LoadOptions(options);
        foreach (NumericUpDown number in new[] { startDelay, scrollDelay, scrollAmount })
            number.ValueChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        foreach (MotionToggle toggle in new[] { autoTop, showRegion, ignoreBottom, autoSave })
            toggle.CheckedChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        method.SelectedIndexChanged += (_, _) =>
        {
            amountRow.Visible = (ScrollMethod)method.SelectedIndex != ScrollMethod.PageDown;
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    private string TextFor(string name) => (resources.GetString(name + ".Text", UiLanguage.Culture) ?? name).Replace("&", "").TrimEnd(':', '：');

    private Control AddRow(string resourceName, Control input, string? text = null)
    {
        TableLayoutPanel row = new() { Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(0, 46), ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(2, 6, 2, 6) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Label caption = new() { Text = text ?? TextFor(resourceName), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 12, 3) };
        input.AccessibleName = caption.Text;
        foreach (NumericUpDown number in input.Controls.OfType<SettingsNumberField>().SelectMany(field => field.Controls.OfType<NumericUpDown>())) number.AccessibleName = caption.Text;
        row.SizeChanged += (_, _) => caption.MaximumSize = new Size(Math.Max(1, row.ClientSize.Width - row.Padding.Horizontal - input.Width - caption.Margin.Horizontal), 0);
        row.Controls.Add(caption, 0, 0);
        row.Controls.Add(input, 1, 0);
        int index = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(row, 0, index);
        return row;
    }

    private static NumericUpDown Number(string name, int minimum, int maximum, int increment) => new SettingsNumericInput()
    {
        Name = name, Minimum = minimum, Maximum = maximum, Increment = increment,
        Width = 100, TextAlign = HorizontalAlignment.Right, Margin = Padding.Empty, Anchor = AnchorStyles.Right
    };

    private static MotionToggle Toggle(string name) => new() { Name = name, Size = new Size(46, 28), Margin = Padding.Empty, Anchor = AnchorStyles.Right };

    private static Control WithUnit(NumericUpDown number, string unit)
    {
        TableLayoutPanel group = new() { Width = 184, Height = 34, ColumnCount = 2, RowCount = 1, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        group.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        group.Controls.Add(new SettingsNumberField(number) { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
        group.Controls.Add(new Label { Name = number.Name + "Unit", Text = unit, AutoSize = true, Anchor = AnchorStyles.Left, Tag = "secondary", Margin = new Padding(8, 0, 0, 0) }, 1, 0);
        return group;
    }

    internal void LoadOptions(ScrollingCaptureOptions options)
    {
        startDelay.Value = Math.Clamp(options.StartDelay, 100, 5000);
        scrollDelay.Value = Math.Clamp(options.ScrollDelay, 100, 5000);
        scrollAmount.Value = Math.Clamp(options.ScrollAmount, 1, 6);
        method.SelectedIndex = Enum.IsDefined(options.ScrollMethod) ? (int)options.ScrollMethod : 0;
        autoTop.Checked = options.AutoScrollTop;
        showRegion.Checked = options.ShowRegion;
        ignoreBottom.Checked = options.AutoIgnoreBottomEdge;
        autoSave.Checked = options.AutoUpload;
        amountRow.Visible = options.ScrollMethod != ScrollMethod.PageDown;
    }

    internal ScrollingCaptureOptions ReadOptions()
    {
        // Commit any text still being edited in the native numeric inputs.
        return new()
        {
            StartDelay = (int)startDelay.Value, ScrollDelay = (int)scrollDelay.Value,
            ScrollMethod = (ScrollMethod)method.SelectedIndex, ScrollAmount = (int)scrollAmount.Value,
            AutoScrollTop = autoTop.Checked, ShowRegion = showRegion.Checked,
            AutoIgnoreBottomEdge = ignoreBottom.Checked, AutoUpload = autoSave.Checked
        };
    }
}

internal sealed class SettingsDisclosure : Button
{
    internal CapturePalette Palette { get; set; } = CapturePalette.Light;
    private bool expanded;
    private readonly UiTween rotation;
    internal bool Expanded
    {
        get => expanded;
        set { expanded = value; rotation.SpringTo(value ? 1 : 0, 180); }
    }
    private bool hovering;

    internal SettingsDisclosure()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
        rotation = new(this, Invalidate);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        float s = DeviceDpi / 96f;
        g.Clear(Parent?.BackColor ?? Palette.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath shape = CaptureDrawing.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 10 * s);
        if (hovering)
        {
            using SolidBrush fill = new(Palette.Hover);
            g.FillPath(fill, shape);
        }
        if (Focused && ShowFocusCues)
        {
            using Pen edge = new(Palette.Accent, 1.5f * s);
            g.DrawPath(edge, shape);
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(2 * s), 0, Math.Max(0, Width - (int)(32 * s)), Height), Palette.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        float x = Width - 14 * s, y = Height / 2f;
        using Pen chevron = new(Palette.Secondary, 1.7f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.TranslateTransform(x, y);
        g.RotateTransform(-180 * rotation.Value);
        g.DrawLines(chevron, new PointF[] { new(-4 * s, -2 * s), new(0, 2 * s), new(4 * s, -2 * s) });
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new DisclosureAccessibleObject(this);

    private sealed class DisclosureAccessibleObject(SettingsDisclosure owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.PushButton;
        public override string? DefaultAction => owner.Text;
        public override void DoDefaultAction() => owner.PerformClick();
        public override AccessibleStates State => base.State | (owner.Expanded ? AccessibleStates.Expanded : AccessibleStates.Collapsed);
    }

    internal void NotifyExpanded()
    {
        Invalidate();
        AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
    }
}
