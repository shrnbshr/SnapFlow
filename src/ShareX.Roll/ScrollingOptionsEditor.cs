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
        Padding = new Padding(0, 0, 0, SettingsLayout.Vertical);
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
        TableLayoutPanel row = new() { Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(0, 56), ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, ButtonSpacing.Vertical, 0, ButtonSpacing.Vertical) };
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
        TableLayoutPanel group = new() { Width = 184, Height = ButtonSpacing.FieldHeight, ColumnCount = 2, RowCount = 1, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
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

internal sealed class SettingsDisclosure : Button, IBackdropCoverage
{
    internal CapturePalette Palette { get; set; } = CapturePalette.Light;
    private bool expanded;
    private readonly UiTween rotation;
    internal bool KeyboardActivation { get; private set; }
    internal bool Expanded
    {
        get => expanded;
        set
        {
            expanded = value;
            if (KeyboardActivation) rotation.SnapTo(value ? 1 : 0);
            else rotation.SpringTo(value ? 1 : 0, 180);
        }
    }
    private readonly UiTween hover;
    private readonly ButtonElevation elevation = new();

    internal SettingsDisclosure()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
        rotation = new(this, Invalidate);
        hover = new(this, Invalidate);
        Disposed += (_, _) => elevation.Dispose();
    }

    protected override void WndProc(ref Message message)
    {
        // Native Space activation raises Click while processing its key message.
        bool previous = KeyboardActivation;
        KeyboardActivation |= message.Msg is 0x0100 or 0x0101 or 0x0102;
        try { base.WndProc(ref message); }
        finally { KeyboardActivation = previous; }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Enter can activate the focused/default button through the containing form.
        bool previous = KeyboardActivation;
        KeyboardActivation = true;
        try { return base.ProcessDialogKey(keyData); }
        finally { KeyboardActivation = previous; }
    }

    protected override bool ProcessMnemonic(char charCode)
    {
        bool previous = KeyboardActivation;
        KeyboardActivation = true;
        try { return base.ProcessMnemonic(charCode); }
        finally { KeyboardActivation = previous; }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover.SpringTo(1, ButtonElevation.ResponseMilliseconds); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover.SpringTo(0, ButtonElevation.ResponseMilliseconds); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    void IBackdropCoverage.PaintBackgroundCoverage(Graphics graphics) => PaintDisclosure(graphics, coverage: true);
    protected override void OnPaint(PaintEventArgs e) => PaintDisclosure(e.Graphics, coverage: false);

    private void PaintDisclosure(Graphics g, bool coverage)
    {
        float s = DeviceDpi / 96f;
        if (Parent is SettingsGroup group) group.PaintChildBackground(this, g, coverage);
        else g.Clear(coverage ? Color.Black : Parent?.BackColor ?? Palette.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF face = ButtonElevation.CompactFaceBounds(ClientSize, s);
        elevation.PaintShadow(g, ClientSize, s, Palette.Background.GetBrightness() < .5f, Enabled && Palette.Style == SoftwareStyle.Default ? hover.Value : 0, compact: true, unified: true);
        float radius = Palette.CornerRadius(Palette.IsWinUI ? CapturePalette.WinUIControlRadius * s :
            Palette.IsMaterial ? MaterialPalette.ControlRadius * s : ButtonElevation.CompactRadius(face, s));
        if (Palette.IsMaterial || Palette.IsWinUI) CaptureDrawing.InsetAntialiasedFace(ref face, ref radius);
        using GraphicsPath shape = CaptureDrawing.Rounded(face, radius);
        float emphasis = Enabled && Palette.IsClaude ? hover.Value : 0;
        Color resting = Palette.IsMaterial ? Expanded ? Palette.Tint : Palette.RaisedSurface : Parent?.BackColor ?? Palette.Surface;
        Color background = Palette.IsWinUI ? Palette.WinUIHoverFill(resting, Enabled ? hover.Value : 0) :
            CaptureDrawing.Blend(resting, ButtonElevation.Surface(Palette), Palette.IsClaude ? emphasis * .4f : hover.Value);
        using SolidBrush fill = new(coverage ? Color.Black : background);
        g.FillPath(fill, shape);
        if (Focused && ShowFocusCues || SystemInformation.HighContrast)
        {
            using Pen edge = new(coverage ? Color.Black : Palette.Accent, 1.5f * s);
            if (Palette.HasShapedControls) CaptureDrawing.DrawInsideBorder(g, face, radius, edge.Color, edge.Width);
            else g.DrawPath(edge, shape);
        }
        if (coverage) return;
        TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(SettingsLayout.Inset * s), 0, Math.Max(0, Width - (int)(2 * SettingsLayout.Inset * s)), Height),
            Palette.IsMaterial && Expanded ? Palette.TintText : CaptureDrawing.Blend(Palette.Text, Palette.Accent, emphasis),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | UiLanguage.TextDirection);
        float x = face.Right - 15 * s, y = Height / 2f;
        using Pen chevron = new(CaptureDrawing.Blend(Palette.Secondary, Palette.Accent, emphasis), 1.7f * s)
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.TranslateTransform(x, y);
        g.RotateTransform(-180 * rotation.Value);
        if (ThemeIcons.TryDraw(g, ThemeIcons.Centered(0, 0, 15 * s), Palette.Style, "chevronDown", chevron.Color)) return;
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
