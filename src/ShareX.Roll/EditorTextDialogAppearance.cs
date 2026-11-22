// GPL-3.0-or-later. Keep the ShareX text and formatting controls while applying the editor palette.
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using ShareX.HelpersLib;
using TextDrawingInputBox = ShareX.ScreenCaptureLib.TextDrawingInputBox;
using TextFontChoice = ShareX.ScreenCaptureLib.TextFontChoice;

namespace ShareX.Roll;

internal static class EditorTextDialogAppearance
{
    private sealed class DialogLayout : TableLayoutPanel, IBackdropBackground, IBackdropCachingScope;

    private sealed class PreviewSurface : Panel
    {
        internal PreviewSurface() =>
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    internal static void Apply(TextDrawingInputBox dialog, CapturePalette palette, bool transparency)
    {
        if (palette.IsMaterial) palette = palette with { Background = palette.Surface };
        bool dark = palette.Background.GetBrightness() < .5f;
        dialog.SuspendLayout();
        dialog.AutoScaleMode = AutoScaleMode.Dpi;
        dialog.AutoScaleDimensions = new SizeF(96, 96);
        Font interfaceFont = new("Microsoft YaHei UI", 10);
        dialog.Font = interfaceFont;
        dialog.Disposed += (_, _) => interfaceFont.Dispose();
        dialog.ClientSize = new Size(648, 388);
        dialog.MinimumSize = new Size(528, 312);
        dialog.BackColor = palette.Background;
        dialog.ForeColor = palette.Text;
        dialog.ApplyHostAppearance(dark, color => PreviewBackground(palette, color));

        TextBox input = dialog.Input;
        Label tip = dialog.Tip;
        FlowLayoutPanel tools = dialog.FormattingBar;
        if (palette.IsMaterial) tools.BackColor = tip.BackColor = palette.Background;
        DialogLayout root = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(16), Margin = Padding.Empty, BackColor = palette.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        tools.Dock = DockStyle.Fill;
        tools.AutoSize = tools.WrapContents = true;
        tools.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        tools.Margin = new Padding(0, 0, 0, 8);
        tools.TabIndex = 1;
        FlowLayoutPanel glyphs = new()
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Padding = Padding.Empty, Margin = Padding.Empty, Anchor = AnchorStyles.Left,
            TabIndex = tools.Controls.Count
        };
        string label = string.Empty;
        foreach (Control field in tools.Controls.Cast<Control>().ToArray())
        {
            field.Anchor = AnchorStyles.Left;
            field.Margin = new Padding(0, 3, 6, 3);
            field.ForeColor = palette.Text;
            field.BackColor = palette.Background;
            switch (field)
            {
                case Label caption:
                    label = caption.Text;
                    break;
                case TextFontChoice fonts:
                    fonts.Width = 148;
                    fonts.AccessibleName = label;
                    StyleFonts(fonts, palette);
                    break;
                case NumericUpDown size:
                    size.AccessibleName = label;
                    size.TextAlign = HorizontalAlignment.Center;
                    int index = tools.Controls.GetChildIndex(size);
                    SettingsNumberField number = new(size)
                    {
                        Size = new Size(78, 30), Anchor = AnchorStyles.Left,
                        Margin = new Padding(0, 3, 6, 3), TabIndex = size.TabIndex
                    };
                    tools.Controls.Add(number);
                    tools.Controls.SetChildIndex(number, index);
                    number.ApplyPalette(palette);
                    break;
                case ButtonBase button:
                    glyphs.Controls.Add(button);
                    button.Size = new Size(31, 30);
                    button.AccessibleName = dialog.ButtonHint(button);
                    DialogChrome.StyleButton(button, palette, quiet: palette.IsMaterial);
                    break;
            }
        }
        tools.Controls.Add(glyphs);
        root.Controls.Add(tools, 0, 0);

        PreviewSurface surface = new()
        {
            Dock = DockStyle.Fill, BackColor = palette.Background, Margin = Padding.Empty, Padding = new Padding(10), TabIndex = 0
        };
        input.BorderStyle = BorderStyle.None;
        input.Dock = DockStyle.Fill;
        input.BackColorChanged += (_, _) => surface.Invalidate();
        input.GotFocus += (_, _) => surface.Invalidate();
        input.LostFocus += (_, _) => surface.Invalidate();
        surface.Paint += (_, e) =>
        {
            float scale = surface.DeviceDpi / 96f;
            Color border = input.Focused ? palette.Accent : palette.Border;
            if (!input.Focused && input.BackColor != palette.Surface)
                border = palette.FieldBorder.IsEmpty ? palette.Secondary : palette.FieldBorder;
            float radius = palette.CornerRadius(palette.IsWinUI ? CapturePalette.WinUIControlRadius : palette.IsMaterial ? MaterialPalette.ControlRadius : 8) * scale;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CaptureDrawing.Rounded(surface.ClientRectangle, radius);
            using SolidBrush fill = new(input.BackColor);
            e.Graphics.FillPath(fill, path);
            CaptureDrawing.DrawInsideBorder(e.Graphics, surface.ClientRectangle, radius, border,
                (palette.IsMaterial && input.Focused ? 1.5f : 1) * scale);
        };
        surface.Resize += (_, _) => surface.Invalidate();
        surface.Controls.Add(input);
        root.Controls.Add(surface, 0, 1);
        UseOverflowScrollbar(input, dark);

        TableLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 12, 0, 0), TabIndex = 2
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Button enter = dialog.EnterKeyButton;
        Button confirm = dialog.ConfirmButton;
        Button cancel = dialog.CancelAction;
        enter.AccessibleName = tip.Text;
        tip.TextChanged += (_, _) => enter.AccessibleName = tip.Text;
        DialogChrome.StyleButton(enter, palette, quiet: palette.IsMaterial);
        DialogChrome.StyleButton(confirm, palette, primary: true);
        DialogChrome.StyleButton(cancel, palette, quiet: palette.IsMaterial);
        enter.Size = new Size(31, 32);
        confirm.Size = cancel.Size = new Size(86, 32);
        enter.Margin = confirm.Margin = new Padding(0, 0, 6, 0);
        cancel.Margin = Padding.Empty;
        enter.Anchor = confirm.Anchor = cancel.Anchor = AnchorStyles.None;
        tip.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        tip.Margin = new Padding(0, 0, 8, 0);
        tip.ForeColor = palette.Secondary;
        CaptureTypography.ApplyDescription(tip, dialog.Font, palette);
        footer.Controls.Add(enter, 0, 0);
        footer.Controls.Add(tip, 1, 0);
        footer.Controls.Add(confirm, 2, 0);
        footer.Controls.Add(cancel, 3, 0);
        root.Controls.Add(footer, 0, 2);
        dialog.Controls.Add(root);
        dialog.ActiveControl = input;

        foreach (ToolStripDropDown menu in dialog.FormattingMenus) DialogChrome.StyleMenu(menu, palette, dialog.Font);
        UiLanguage.ApplyTextDirection(dialog);
        // Material You has a solid client. Let the native multiline edit paint directly,
        // including line backgrounds, selection and composition from the IME.
        DialogChrome.ApplyFrame(dialog, palette, transparency, frameMaterialOnly: palette.IsMaterial);
        dialog.ResumeLayout(true);
    }

    private static void StyleFonts(TextFontChoice fonts, CapturePalette palette)
    {
        fonts.BackColor = palette.Surface;
        fonts.FlatStyle = FlatStyle.Flat;
        fonts.EnableHostPainting();
        fonts.DrawMode = DrawMode.OwnerDrawFixed;
        void SetItemHeight() => fonts.ItemHeight = fonts.Font.Height + (int)Math.Round(5 * fonts.DeviceDpi / 96f);
        fonts.FontChanged += (_, _) => SetItemHeight();
        fonts.DpiChangedAfterParent += (_, _) => SetItemHeight();
        SetItemHeight();
        fonts.SelectedIndexChanged += (_, _) => fonts.Invalidate();
        fonts.DropDown += (_, _) => fonts.Invalidate();
        fonts.DropDownClosed += (_, _) => fonts.Invalidate();
        fonts.GotFocus += (_, _) => fonts.Invalidate();
        fonts.LostFocus += (_, _) => fonts.Invalidate();
        bool hovering = false;
        if (palette.IsWinUI)
        {
            fonts.MouseEnter += (_, _) => { hovering = true; fonts.Invalidate(); };
            fonts.MouseLeave += (_, _) => { hovering = false; fonts.Invalidate(); };
            fonts.EnabledChanged += (_, _) => { if (!fonts.Enabled) hovering = false; fonts.Invalidate(); };
        }
        fonts.Paint += (_, e) =>
        {
            float s = fonts.DeviceDpi / 96f;
            Graphics g = e.Graphics;
            g.Clear(palette.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = palette.CornerRadius(palette.IsWinUI ? CapturePalette.WinUIControlRadius : palette.IsMaterial ? MaterialPalette.FieldRadius : 6) * s;
            using var path = CaptureDrawing.Rounded(fonts.ClientRectangle, radius);
            Color fillColor = fonts.DroppedDown ? palette.IsMaterial || palette.IsWinUI ? palette.Tint : palette.Hover :
                palette.IsWinUI && hovering && fonts.Enabled ? palette.WinUIHoverFill(palette.Surface) : palette.Surface;
            using SolidBrush fill = new(fillColor);
            g.FillPath(fill, path);
            Color edge = fonts.Focused ? palette.Accent : palette.FieldBorder.IsEmpty ? palette.Border : palette.FieldBorder;
            CaptureDrawing.DrawInsideBorder(g, fonts.ClientRectangle, radius, edge, (palette.IsMaterial && fonts.Focused ? 1.5f : 1) * s);
            int inset = (int)Math.Round(8 * s), arrow = (int)Math.Round(24 * s);
            bool rtl = fonts.RightToLeft == RightToLeft.Yes;
            Rectangle text = new(rtl ? arrow : inset, 0, Math.Max(0, fonts.Width - arrow - inset), fonts.Height);
            TextRenderer.DrawText(g, fonts.Text, fonts.Font, text, palette.Text, TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | UiLanguage.TextDirection);
            float x = rtl ? arrow / 2f : fonts.Width - arrow / 2f, y = fonts.Height / 2f;
            if (ThemeIcons.TryDraw(g, ThemeIcons.Centered(x, y, 13 * s), palette.Style, "chevronDown", palette.Secondary)) return;
            using Pen chevron = new(palette.Secondary, 1.4f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(chevron, [new PointF(x - 3 * s, y - s), new(x, y + 2 * s), new(x + 3 * s, y - s)]);
        };
        fonts.DrawItem += (_, e) =>
        {
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using SolidBrush fill = new(selected ? palette.Tint : palette.Surface);
            e.Graphics.FillRectangle(fill, e.Bounds);
            if (e.Index < 0 || e.Index >= fonts.Items.Count) return;
            Rectangle text = e.Bounds;
            text.Inflate(-3, 0);
            TextRenderer.DrawText(e.Graphics, fonts.GetItemText(fonts.Items[e.Index]), fonts.Font, text,
                palette.IsMaterial && selected ? palette.TintText : palette.Text,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | UiLanguage.TextDirection);
            if (selected) e.DrawFocusRectangle();
        };
    }

    // The existing edit keeps its scrolling and IME state when the native bar changes visibility.
    private static void UseOverflowScrollbar(TextBox input, bool dark)
    {
        const int vertical = 1, getLineCount = 0x00BA;
        void Refresh()
        {
            if (!input.IsHandleCreated) return;
            long lines = NativeMethods.SendMessage(input.Handle, getLineCount, IntPtr.Zero, IntPtr.Zero).ToInt64();
            ShowScrollBar(input.Handle, vertical, lines * input.Font.Height > input.ClientSize.Height);
        }
        input.HandleCreated += (_, _) =>
        {
            if (dark) NativeMethods.SetWindowTheme(input.Handle, "DarkMode_Explorer", null);
            Refresh();
        };
        input.TextChanged += (_, _) => Refresh();
        input.FontChanged += (_, _) => Refresh();
        input.SizeChanged += (_, _) => Refresh();
        Refresh();
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowScrollBar(IntPtr window, int bar, [MarshalAs(UnmanagedType.Bool)] bool show);

    private static Color PreviewBackground(CapturePalette palette, Color ink)
    {
        Color other = palette.Background.GetBrightness() < .5f ? CapturePalette.For(AppearanceMode.Light, palette.Style).Surface :
            CapturePalette.For(AppearanceMode.Dark, palette.Style).Surface;
        if (Contrast(ink, palette.Surface) >= 4.5) return palette.Surface;
        if (Contrast(ink, other) >= 4.5) return other;
        return Contrast(ink, Color.White) >= Contrast(ink, Color.Black) ? Color.White : Color.Black;
    }

    private static double Contrast(Color ink, Color background)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        static double Light(Color color) => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        double a = Light(ink) + .05, b = Light(background) + .05;
        return Math.Max(a, b) / Math.Min(a, b);
    }
}
