// GPL-3.0-or-later. Native tool items keep the ShareX commands, shortcuts and accessibility.
using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using System.Drawing.Drawing2D;
using static ShareX.Roll.Localization.UiText;

namespace ShareX.Roll;

internal static class EditorChrome
{
    internal static void Install(RegionCaptureForm editor, ToolStrip tools, CapturePalette palette, bool transparency = true)
    {
        Form window = (Form)tools.Parent!;
        float dpi = editor.DeviceDpi / 96f;
        int S(int value) => (int)Math.Round(value * dpi);
        window.AutoSize = false;
        window.AutoScaleMode = AutoScaleMode.None;
        window.Size = new(editor.ClientSize.Width, S(148));
        tools.AutoSize = false;
        // Native ToolStrip initialization can otherwise clamp the first layout to
        // its upstream 30px minimum until the queued Shown layout corrects it.
        tools.MinimumSize = new Size(tools.MinimumSize.Width, S(56));
        tools.ShowItemToolTips = true;
        tools.TabStop = true;
        tools.AccessibleName = L("EditorTools");
        Panel header = new() { Name = "editorHeader", Bounds = new(0, 0, window.Width, S(80)) };
        Label title = new() { Name = "editorTitle", Text = L("Edit"), AutoSize = false, Bounds = new(S(28), S(20), S(230), S(24)) };
        Label status = new() { Name = "editorStatus", AutoEllipsis = true, Bounds = new(S(28), S(48), S(280), S(20)) };
        Font? titleFont = null, statusFont = null;
        ToolStripEx actions = new()
        {
            Name = "editorActions", AutoSize = false, Dock = DockStyle.None, ClickThrough = true,
            GripStyle = ToolStripGripStyle.Hidden, ShowItemToolTips = true, TabStop = true,
            Padding = new Padding(0), AccessibleName = L("EditorActions")
        };
        header.Controls.AddRange([title, status, actions]);
        window.Controls.Add(header);
        ToolStripButton confirm = new(L("ConfirmCrop")) { Name = "editorConfirmCrop", DisplayStyle = ToolStripItemDisplayStyle.Text };
        ToolStripButton cancel = new(L("Cancel")) { Name = "editorCancelCrop", DisplayStyle = ToolStripItemDisplayStyle.Text };
        confirm.Click += (_, _) => editor.ConfirmEditorCrop();
        cancel.Click += (_, _) => editor.CancelEditorCrop();
        // Crop commands live with tools; output commands have their own clear hierarchy.
        tools.Items.Insert(1, confirm);
        tools.Items.Insert(2, cancel);
        foreach (string name in new[] { "editorCopy", "editorSave", "editorSaveAs", "editorApply" })
        {
            ToolStripItem item = tools.Items[name]!;
            tools.Items.Remove(item);
            item.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            item.ToolTipText = item.Text;
            actions.Items.Add(item);
            if (name == "editorApply") item.Overflow = ToolStripItemOverflow.Never;
        }
        // Separators left behind by the original output group carry no meaning in the tools row.
        foreach (ToolStripSeparator separator in tools.Items.OfType<ToolStripSeparator>().ToArray())
        {
            tools.Items.Remove(separator);
            separator.Dispose();
        }
        ToolStripItem crop = tools.Items["editorCrop"]!;
        tools.Items.Remove(crop);
        tools.Items.Insert(1, crop);
        ToolStripDropDownItem styleMenu = (ToolStripDropDownItem)tools.Items["editorStyle"]!;
        foreach (string name in new[] { "editorStrokeColor", "editorHighlightColor" })
        {
            ToolStripItem advancedColor = tools.Items[name]!;
            tools.Items.Remove(advancedColor);
            advancedColor.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            styleMenu.DropDownItems.Insert(0, advancedColor);
        }
        ToolStripDropDownButton annotationColor = new(L("AnnotationColor"))
        {
            Name = "editorAnnotationColor", DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            Overflow = ToolStripItemOverflow.Never
        };
        Bitmap Swatch(Color color)
        {
            Bitmap image = new(S(18), S(18));
            using Graphics g = Graphics.FromImage(image);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using SolidBrush fill = new(color);
            using Pen border = new(palette.Secondary, Math.Max(1, dpi));
            using GraphicsPath path = CaptureDrawing.Rounded(new RectangleF(dpi, dpi, image.Width - 2 * dpi, image.Height - 2 * dpi), 3 * dpi);
            g.FillPath(fill, path);
            g.DrawPath(border, path);
            return image;
        }
        foreach (Color color in new[] { Color.Red, Color.Orange, Color.Gold, Color.ForestGreen, Color.RoyalBlue, Color.MediumPurple, Color.Black, Color.White })
        {
            ToolStripMenuItem choice = new("#" + (color.ToArgb() & 0xFFFFFF).ToString("X6")) { Tag = color, Image = Swatch(color), Name = "editorColor" + color.ToArgb().ToString("X8") };
            choice.Click += (_, _) => editor.SetEditorAnnotationColor(color);
            choice.Disposed += (_, _) => choice.Image?.Dispose();
            annotationColor.DropDownItems.Add(choice);
        }
        annotationColor.DropDownItems.Add(new ToolStripSeparator());
        ToolStripMenuItem customColor = new(L("AnnotationCustomColor")) { Name = "editorCustomColor" };
        customColor.Click += (_, _) => editor.ChooseEditorAnnotationColor();
        annotationColor.DropDownItems.Add(customColor);
        annotationColor.Disposed += (_, _) => annotationColor.Image?.Dispose();
        ToolStripItem fillColor = tools.Items["editorFillColor"]!;
        tools.Items.Remove(fillColor);
        fillColor.Text = L("AnnotationFill");
        fillColor.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        fillColor.Overflow = ToolStripItemOverflow.Never;
        crop.Overflow = ToolStripItemOverflow.Never;
        confirm.Overflow = cancel.Overflow = ToolStripItemOverflow.Never;
        ToolStripItem select = tools.Items.Cast<ToolStripItem>().Single(item => Equals(item.Tag, ShapeType.ToolSelect));
        tools.Items.Remove(select);
        tools.Items.Insert(0, select);
        tools.Items.Remove(crop);
        tools.Items.Insert(1, crop);
        tools.Items.Insert(2, annotationColor);
        tools.Items.Insert(3, fillColor);
        foreach (ToolStripItem item in tools.Items)
        {
            item.AccessibleName = item.Text;
            item.ToolTipText = item.Text;
            if (item is ToolStripDropDownItem dropdown) dropdown.DropDownClosed += (_, _) => editor.Invalidate();
            if (item.Tag is ShapeType type && type != ShapeType.ToolCrop)
                item.DisplayStyle = ToolStripItemDisplayStyle.Image;
        }
        select.ToolTipText = select.Text + Environment.NewLine + L("AnnotationDeleteHint");
        void RefreshState()
        {
            confirm.Visible = cancel.Visible = editor.IsEditorCropPending;
            annotationColor.Visible = !editor.IsEditorCropPending;
            annotationColor.Enabled = editor.CanChangeEditorAnnotationColor;
            Color color = editor.EditorAnnotationColor;
            if (annotationColor.Tag is not Color previous || previous.ToArgb() != color.ToArgb())
            {
                Image? previousImage = annotationColor.Image;
                annotationColor.Image = Swatch(color);
                annotationColor.Tag = color;
                previousImage?.Dispose();
                foreach (ToolStripMenuItem choice in annotationColor.DropDownItems.OfType<ToolStripMenuItem>())
                    if (choice.Tag is Color value) choice.Checked = value.ToArgb() == color.ToArgb();
            }
            status.Text = editor.IsEditorCropPending ? L("CropEdgesHint") : L("AnnotationDeleteHint");
        }
        editor.EditorStateChanged += RefreshState;
        editor.EditorFeedback += text => status.Text = text;
        void LayoutChrome()
        {
            window.Width = editor.ClientSize.Width;
            header.Width = window.Width;
            int actionWidth = Math.Min(window.Width - S(48), actions.GetPreferredSize(Size.Empty).Width);
            actions.Bounds = new(window.Width - actionWidth - S(24), S(16), actionWidth, S(48));
            title.Visible = status.Visible = actions.Left > S(320);
            tools.Bounds = new(S(24), S(80), Math.Max(S(48), window.Width - S(48)), S(56));
            tools.PerformLayout();
            actions.PerformLayout();
        }
        void Style()
        {
            window.Size = new(editor.ClientSize.Width, S(148));
            window.BackColor = header.BackColor = tools.BackColor = actions.BackColor = palette.Background;
            title.BackColor = status.BackColor = palette.Background;
            window.ForeColor = title.ForeColor = tools.ForeColor = actions.ForeColor = palette.Text;
            status.ForeColor = palette.Secondary;
            Font? oldTitleFont = titleFont, oldStatusFont = statusFont;
            title.Font = titleFont = new Font(editor.Font.FontFamily, 14, FontStyle.Bold);
            status.Font = statusFont = new Font(editor.Font.FontFamily, 10);
            oldTitleFont?.Dispose();
            oldStatusFont?.Dispose();
            foreach (ToolStrip strip in new[] { tools, actions })
            {
                strip.Font = editor.Font;
                strip.ImageScalingSize = new(S(20), S(20));
                // The visible faces align with the workspace's 28px inset; the 4px halo is inside each hit target.
                strip.Padding = strip == tools ? new Padding(0, S(4), 0, S(4)) : Padding.Empty;
                foreach (ToolStripItem item in strip.Items)
                {
                    item.AutoSize = false;
                    if (item is ToolStripSeparator)
                    {
                        item.Size = new(S(12), S(ButtonSpacing.ToolHeight));
                        item.Padding = Padding.Empty;
                        item.Margin = new Padding(S(4), 0, S(4), 0);
                        continue;
                    }
                    item.Padding = item.DisplayStyle == ToolStripItemDisplayStyle.Image ? Padding.Empty : new Padding(S(12), 0, S(12), 0);
                    item.Margin = Padding.Empty;
                    int width = item.DisplayStyle == ToolStripItemDisplayStyle.Image ? S(48) :
                        TextRenderer.MeasureText(item.Text, strip.Font, Size.Empty, TextFormatFlags.NoPadding).Width +
                        S(item.DisplayStyle == ToolStripItemDisplayStyle.Text ? 32 : 60);
                    if (item is ToolStripDropDownButton) width += S(16);
                    item.Size = new(width, S(48));
                    item.AccessibleName = item.Text;
                }
                strip.Renderer = new EditorToolStripRenderer(strip, palette);
                EditorOverflow.Install(strip, strip == tools ? L("EditorMoreTools") : L("EditorActions"), palette, transparency);
            }
            RefreshState();
            UiLanguage.ApplyTextDirection(window);
            LayoutChrome();
        }
        // The native child toolbar shares the editor's backdrop rather than stacking two glass windows.
        // The library finishes its generic theme pass before Show. Style in Load so
        // the child HWND never becomes visible with the default toolbar renderer.
        window.Load += (_, _) => Style();
        editor.Shown += (_, _) => LayoutChrome();
        editor.Resize += (_, _) => LayoutChrome();
        // Child disposal can reset Control.Font to a shared default. Dispose only fonts we own.
        window.Disposed += (_, _) => { titleFont?.Dispose(); statusFont?.Dispose(); };
    }
}

internal sealed class EditorToolStripRenderer : ToolStripProfessionalRenderer
{
    internal bool PaintingCoverage { get; set; }
    internal bool PaintingShadows { get; set; } = true;
    private bool keyboardNavigation;
    private sealed class InputFeedback(Action<bool> changed) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message message)
        {
            // Observe Tab on its previous control before native ToolStrip navigation consumes it.
            if (message.Msg is 0x100 or 0x104) changed(true);
            else if (message.Msg is >= 0x200 and <= 0x20E) changed(false);
            return false;
        }
    }
    private sealed class Feedback
    {
        internal readonly UiTween Hover, Press, Selected;
        internal readonly EditorButtonShadow Elevation = new();
        internal Feedback(ToolStrip strip, ToolStripItem item)
        {
            void Repaint() { if (!strip.IsDisposed) strip.Invalidate(item.Bounds); }
            Hover = new(strip, Repaint); Press = new(strip, Repaint); Selected = new(strip, Repaint);
            Selected.SnapTo(item is ToolStripButton { Checked: true } ? 1 : 0);
            item.MouseEnter += (_, _) => Hover.SpringTo(1, ButtonElevation.ResponseMilliseconds);
            item.MouseLeave += (_, _) => { Hover.SpringTo(0, ButtonElevation.ResponseMilliseconds); Press.SpringTo(0); };
            item.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) Press.SnapTo(1); };
            item.MouseUp += (_, _) => Press.SpringTo(0, 130);
            item.EnabledChanged += (_, _) => { if (!item.Enabled) { Hover.SnapTo(0); Press.SnapTo(0); } };
            if (item is ToolStripButton button) button.CheckedChanged += (_, _) => Selected.SpringTo(button.Checked ? 1 : 0, 150);
            item.Disposed += (_, _) => { Hover.Dispose(); Press.Dispose(); Selected.Dispose(); Elevation.Dispose(); };
            strip.Disposed += (_, _) => Elevation.Dispose();
        }
    }
    private readonly CapturePalette palette;
    private readonly Dictionary<ToolStripItem, Feedback> states = new();
    internal EditorToolStripRenderer(ToolStrip strip, CapturePalette palette)
    {
        this.palette = palette;
        RoundedEdges = false;
        InputFeedback input = new(value =>
        {
            if (keyboardNavigation == value) return;
            keyboardNavigation = value;
            if (!strip.IsDisposed) strip.Invalidate();
        });
        Application.AddMessageFilter(input);
        strip.Disposed += (_, _) => Application.RemoveMessageFilter(input);
        foreach (ToolStripItem item in strip.Items) Track(item);
        Track(strip.OverflowButton);
        void Track(ToolStripItem item)
        {
            states[item] = new(strip, item);
            item.MouseEnter += (_, _) => keyboardNavigation = false;
            item.MouseDown += (_, _) => keyboardNavigation = false;
        }
        strip.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Tab or Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.Space or Keys.Enter)
                keyboardNavigation = true;
            ToolStripItem? item = strip.Items.Cast<ToolStripItem>().FirstOrDefault(item => item.Selected);
            if (e.KeyCode is Keys.Space or Keys.Enter && item?.Enabled == true && states.TryGetValue(item, out Feedback? state)) state.Press.SnapTo(1);
        };
        strip.PreviewKeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Tab or Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End)
            {
                keyboardNavigation = true;
                strip.Invalidate();
            }
        };
        strip.KeyUp += (_, _) => { foreach (Feedback state in states.Values) if (state.Press.Value > 0) state.Press.SpringTo(0, 110); };
        strip.LostFocus += (_, _) => { foreach (Feedback state in states.Values) state.Press.SpringTo(0, 110); };
    }
    private Color Foreground(ToolStripItem item) => PaintingCoverage ? Color.Black : !item.Enabled ? palette.Secondary :
        item.Name == "editorApply" ? palette.AccentText : palette.Text;
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) =>
        e.Graphics.Clear(PaintingCoverage ? Color.White : palette.Background);
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (!e.ToolStrip.IsDropDown || !SystemInformation.HighContrast) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float dpi = e.ToolStrip.DeviceDpi / 96f;
        using GraphicsPath path = CaptureDrawing.Rounded(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 10 * dpi);
        using Pen border = new(palette.Border);
        e.Graphics.DrawPath(border, path);
    }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item is { } item ? Foreground(item) : palette.Text;
        if (e.Item?.GetCurrentParent() is { IsDropDown: true } menu)
        {
            Rectangle rect = EditorMenuLayout.AccessoryBounds(e.Item, menu.DeviceDpi);
            float dpi = menu.DeviceDpi / 96f, x = rect.X + rect.Width / 2f, y = rect.Y + rect.Height / 2f;
            float direction = e.Item.RightToLeft == RightToLeft.Yes ? -1 : 1;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using Pen pen = new(e.ArrowColor, 1.5f * dpi) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            e.Graphics.DrawLines(pen, [new PointF(x - direction * 2 * dpi, y - 4 * dpi),
                new(x + direction * 2 * dpi, y), new(x - direction * 2 * dpi, y + 4 * dpi)]);
            return;
        }
        base.OnRenderArrow(e);
    }
    protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
    {
        Background(e);
        float dpi = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        float centerX = e.Item.Width / 2f, centerY = e.Item.Height / 2f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using SolidBrush dots = new(Foreground(e.Item));
        for (int i = -1; i <= 1; i++) e.Graphics.FillEllipse(dots, centerX + i * 5 * dpi - 1.3f * dpi, centerY - 1.3f * dpi, 2.6f * dpi, 2.6f * dpi);
    }
    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) => Background(e);
    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => Background(e);
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) => Background(e);
    protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e) => Background(e);
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) { }
    private void Background(ToolStripItemRenderEventArgs e)
    {
        ToolStripItem item = e.Item;
        Feedback? state = null;
        if (e.ToolStrip?.IsDropDown != true) states.TryGetValue(item, out state);
        float selected = state?.Selected.Value ?? (item is ToolStripButton { Checked: true } ? 1 : 0);
        float hover = state?.Hover.Value ?? (item.Selected ? 1 : 0);
        float press = state?.Press.Value ?? 0;
        Color surface = palette.Background;
        bool menu = e.ToolStrip?.IsDropDown == true;
        Color fill = Mix(surface, menu ? palette.Hover : ButtonElevation.Surface(palette), hover);
        if (item.Enabled) fill = Mix(fill, palette.Tint, press);
        // Selection and transient press share the workspace edit button's pressed tint and normal ink.
        // Keep each theme's own pairing: light blue/dark ink, or dark blue/light ink.
        fill = Mix(fill, palette.Tint, selected);
        if (item.Name == "editorApply") fill = item.Enabled
            ? Mix(palette.Accent, palette.AccentText, press * .12f) : palette.Accent;
        if (PaintingCoverage) fill = item.Name == "editorApply" ? Color.Black : CaptureButtonPainter.Coverage((1 - hover) * (1 - selected) * (1 - press));
        float dpi = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        // WinForms stretches the overflow hit target to the row height; its visible face stays aligned with other tools.
        float height = Math.Min(40 * dpi, item.Height - 8 * dpi);
        RectangleF rect = menu ? new RectangleF(dpi, dpi, item.Width - 2 * dpi, item.Height - 2 * dpi) :
            new RectangleF(4 * dpi, (item.Height - height) / 2, item.Width - 8 * dpi, height);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (!menu && PaintingShadows && state != null)
            state.Elevation.Paint(e.Graphics, item.Size, rect, 10 * dpi, dpi, palette.Background.GetBrightness() < .5f,
                item.Enabled ? hover * (1 - press) : 0);
        using GraphicsPath path = CaptureDrawing.Rounded(rect, (menu ? 7 : 10) * dpi);
        using SolidBrush brush = new(fill);
        e.Graphics.FillPath(brush, path);
        if (menu && item is ToolStripMenuItem { Checked: true, Tag: Color })
            CheckMark(e.Graphics, item, EditorMenuLayout.AccessoryBounds(item, e.ToolStrip!.DeviceDpi), dpi);
        bool keyboardFocus = !menu && keyboardNavigation && item.Selected && e.ToolStrip is { ContainsFocus: true, IsHandleCreated: true } strip &&
            (NativeMethods.SendMessage(strip.Handle, (uint)WindowsMessages.QUERYUISTATE, IntPtr.Zero, IntPtr.Zero).ToInt64() & 1) == 0;
        if (SystemInformation.HighContrast || keyboardFocus)
        {
            using Pen pen = new(PaintingCoverage ? Color.Black : keyboardFocus ? palette.Accent : palette.Border);
            e.Graphics.DrawPath(pen, path);
        }
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Foreground(e.Item);
        e.TextFormat |= UiLanguage.TextDirection;
        Rectangle textBounds = e.TextRectangle;
        if (e.Item.GetCurrentParent() is { IsDropDown: true } menu && e.Item is not ToolStripControlHost)
        {
            bool shortcut = EditorMenuLayout.ShortcutText(e.Item).Length > 0 && e.Text != e.Item.Text;
            bool rtl = e.Item.RightToLeft == RightToLeft.Yes;
            textBounds = EditorMenuLayout.TextBounds(e.Item, e.TextFont ?? e.Item.Font, menu.DeviceDpi, shortcut);
            e.TextFormat &= ~(TextFormatFlags.HorizontalCenter | TextFormatFlags.Right | TextFormatFlags.Bottom);
            e.TextFormat |= TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                (shortcut != rtl ? TextFormatFlags.Right : TextFormatFlags.Left);
        }
        else if (e.Item.GetCurrentParent()?.IsDropDown != true && e.Item.DisplayStyle == ToolStripItemDisplayStyle.Text)
        {
            float dpi = (e.Item.GetCurrentParent()?.DeviceDpi ?? 96) / 96f;
            int inset = (int)Math.Round(4 * dpi);
            textBounds = new(inset, 0, Math.Max(0, e.Item.Width - 2 * inset), e.Item.Height);
            e.TextFormat &= ~(TextFormatFlags.Right | TextFormatFlags.Bottom);
            e.TextFormat |= TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        }
        else if (e.Item.GetCurrentParent()?.IsDropDown != true && e.Item.DisplayStyle == ToolStripItemDisplayStyle.ImageAndText)
        {
            float dpi = (e.Item.GetCurrentParent()?.DeviceDpi ?? 96) / 96f;
            bool rtl = e.Item.RightToLeft == RightToLeft.Yes;
            int leading = (int)Math.Round((4 + 12 + 20 + 8) * dpi);
            int trailing = (int)Math.Round((4 + 12 + (e.Item is ToolStripDropDownButton ? 16 : 0)) * dpi);
            textBounds = new(rtl ? trailing : leading, 0, Math.Max(0, e.Item.Width - leading - trailing), e.Item.Height);
            e.TextFormat &= ~(TextFormatFlags.HorizontalCenter | TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.Bottom);
            e.TextFormat |= TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                (rtl ? TextFormatFlags.Right : TextFormatFlags.Left);
        }
        // The professional renderer replaces disabled text with SystemColors.GrayText, which loses contrast in dark menus.
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, textBounds, e.TextColor, e.TextFormat);
    }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // Color rows keep their swatch and draw the selected mark in the trailing accessory column.
        if (e.Item.Tag is Color) return;
        float dpi = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        Rectangle rect = e.Item.GetCurrentParent()?.IsDropDown == true
            ? EditorMenuLayout.IconBounds(e.Item, e.ToolStrip!.DeviceDpi)
            : e.ImageRectangle;
        CheckMark(e.Graphics, e.Item, rect, dpi);
    }
    private void CheckMark(Graphics graphics, ToolStripItem item, Rectangle rect, float dpi)
    {
        float x = rect.X + rect.Width / 2f, y = rect.Y + rect.Height / 2f;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using Pen pen = new(Foreground(item), 1.6f * dpi) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        graphics.DrawLines(pen, [new PointF(x - 5 * dpi, y), new(x - 1 * dpi, y + 4 * dpi), new(x + 6 * dpi, y - 4 * dpi)]);
    }
    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        Rectangle imageBounds = e.ImageRectangle;
        if (e.Item.GetCurrentParent() is { IsDropDown: true } menu)
            imageBounds = EditorMenuLayout.IconBounds(e.Item, menu.DeviceDpi);
        else if (e.Item.DisplayStyle == ToolStripItemDisplayStyle.ImageAndText)
        {
            float dpi = (e.Item.GetCurrentParent()?.DeviceDpi ?? 96) / 96f;
            int edge = (int)Math.Round((4 + 12) * dpi), size = (int)Math.Round(20 * dpi);
            imageBounds = new(e.Item.RightToLeft == RightToLeft.Yes ? e.Item.Width - edge - size : edge,
                (e.Item.Height - size) / 2, size, size);
        }
        if (EditorIcons.HasIcon(e.Item))
        {
            Color ink = Foreground(e.Item);
            EditorIcons.Draw(e.Graphics, imageBounds, e.Item.Tag as ShapeType?, e.Item.Name ?? "", ink);
        }
        else if (PaintingCoverage) e.Graphics.FillRectangle(Brushes.Black, imageBounds);
        else base.OnRenderItemImage(new ToolStripItemImageRenderEventArgs(e.Graphics, e.Item, e.Image, imageBounds)); // Preserve native swatch/disabled-image behavior.
    }
    private static Color Mix(Color a, Color b, float value) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * value), (int)(a.G + (b.G - a.G) * value), (int)(a.B + (b.B - a.B) * value));
}
