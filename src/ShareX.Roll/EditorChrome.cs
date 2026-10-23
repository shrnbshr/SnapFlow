// GPL-3.0-or-later. Native tool items keep the ShareX commands, shortcuts and accessibility.
using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using System.Drawing.Drawing2D;
using static ShareX.Roll.Localization.UiText;

namespace ShareX.Roll;

internal static class EditorChrome
{
    internal static void Install(RegionCaptureForm editor, ToolStrip tools, CapturePalette palette)
    {
        Form window = (Form)tools.Parent!;
        float dpi = editor.DeviceDpi / 96f;
        int S(int value) => (int)Math.Round(value * dpi);
        window.AutoSize = false;
        window.AutoScaleMode = AutoScaleMode.None;
        window.Size = new(editor.ClientSize.Width, S(112));
        tools.AutoSize = false;
        tools.ShowItemToolTips = true;
        tools.TabStop = true;
        tools.AccessibleName = L("EditorTools");
        Panel header = new() { Name = "editorHeader", Bounds = new(0, 0, window.Width, S(62)) };
        Label title = new() { Text = L("Edit"), AutoSize = false, Bounds = new(S(20), S(10), S(230), S(23)) };
        Label status = new() { Name = "editorStatus", AutoEllipsis = true, Bounds = new(S(20), S(35), S(280), S(21)) };
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
            int actionWidth = Math.Min(window.Width - S(24), actions.GetPreferredSize(Size.Empty).Width + S(8));
            actions.Bounds = new(window.Width - actionWidth - S(12), S(12), actionWidth, S(38));
            title.Visible = status.Visible = actions.Left > S(300);
            tools.Bounds = new(0, S(62), window.Width, S(50));
            tools.PerformLayout();
            actions.PerformLayout();
        }
        void Style()
        {
            window.Size = new(editor.ClientSize.Width, S(112));
            window.BackColor = header.BackColor = tools.BackColor = actions.BackColor = palette.Surface;
            title.BackColor = status.BackColor = palette.Surface;
            window.ForeColor = title.ForeColor = tools.ForeColor = actions.ForeColor = palette.Text;
            status.ForeColor = palette.Secondary;
            title.Font = new Font(editor.Font.FontFamily, 11, FontStyle.Bold);
            status.Font = new Font(editor.Font.FontFamily, 9);
            foreach (ToolStrip strip in new[] { tools, actions })
            {
                strip.Font = editor.Font;
                strip.ImageScalingSize = new(S(18), S(18));
                strip.Padding = new Padding(S(12), S(6), S(12), S(6));
                foreach (ToolStripItem item in strip.Items)
                {
                    item.AutoSize = false;
                    item.Padding = new Padding(S(6), 0, S(6), 0);
                    item.Margin = new Padding(S(2), 0, S(2), 0);
                    int width = item.DisplayStyle == ToolStripItemDisplayStyle.Image ? S(36) :
                        TextRenderer.MeasureText(item.Text, strip.Font).Width + S(item.DisplayStyle == ToolStripItemDisplayStyle.Text ? 20 : 42);
                    item.Size = new(width, S(36));
                    item.AccessibleName = item.Text;
                }
                strip.Renderer = new EditorToolStripRenderer(strip, palette);
                EditorOverflow.Install(strip, strip == tools ? L("EditorMoreTools") : L("EditorActions"));
            }
            tools.Paint += (_, e) => { using Pen line = new(palette.Border); e.Graphics.DrawLine(line, 0, 0, tools.Width, 0); };
            RefreshState();
            LayoutChrome();
        }
        // The library applies its theme immediately before showing the owned toolbar window.
        window.Shown += (_, _) => Style();
        editor.Shown += (_, _) => LayoutChrome();
        editor.Resize += (_, _) => LayoutChrome();
        window.Disposed += (_, _) => { title.Font.Dispose(); status.Font.Dispose(); };
    }
}

internal sealed class EditorToolStripRenderer : ToolStripProfessionalRenderer
{
    private sealed class Feedback
    {
        internal readonly UiTween Hover, Press, Selected;
        internal Feedback(ToolStrip strip, ToolStripItem item)
        {
            void Repaint() { if (!strip.IsDisposed) strip.Invalidate(item.Bounds); }
            Hover = new(strip, Repaint); Press = new(strip, Repaint); Selected = new(strip, Repaint);
            Selected.SnapTo(item is ToolStripButton { Checked: true } ? 1 : 0);
            item.MouseEnter += (_, _) => Hover.AnimateTo(1, 110);
            item.MouseLeave += (_, _) => { Hover.AnimateTo(0, 150); Press.SpringTo(0); };
            item.MouseDown += (_, _) => Press.SnapTo(1);
            item.MouseUp += (_, _) => Press.SpringTo(0, 130);
            item.EnabledChanged += (_, _) => { if (!item.Enabled) { Hover.SnapTo(0); Press.SnapTo(0); } };
            if (item is ToolStripButton button) button.CheckedChanged += (_, _) => Selected.SpringTo(button.Checked ? 1 : 0, 150);
            item.Disposed += (_, _) => { Hover.Dispose(); Press.Dispose(); Selected.Dispose(); };
        }
    }
    private readonly CapturePalette palette;
    private readonly Dictionary<ToolStripItem, Feedback> states = new();
    private readonly UiTween entrance;
    internal EditorToolStripRenderer(ToolStrip strip, CapturePalette palette)
    {
        this.palette = palette;
        RoundedEdges = false;
        entrance = new(strip, strip.Invalidate);
        foreach (ToolStripItem item in strip.Items) states[item] = new(strip, item);
        entrance.AnimateTo(1, 170);
    }
    private Color Foreground(ToolStripItem item) => !item.Enabled ? palette.Secondary :
        item.Name == "editorApply" ? palette.AccentText : item is ToolStripButton { Checked: true } ? palette.Accent : palette.Text;
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(palette.Surface);
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (!e.ToolStrip.IsDropDown) return;
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
        if (e.Item?.GetCurrentParent()?.IsDropDown == true) e.Direction = ArrowDirection.Right;
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
    private void Background(ToolStripItemRenderEventArgs e)
    {
        ToolStripItem item = e.Item;
        Feedback? state = null;
        if (e.ToolStrip?.IsDropDown != true) states.TryGetValue(item, out state);
        float selected = state?.Selected.Value ?? (item is ToolStripButton { Checked: true } ? 1 : 0);
        float hover = state?.Hover.Value ?? (item.Selected ? 1 : 0);
        float press = state?.Press.Value ?? 0;
        Color fill = item.Name == "editorApply" ? palette.Accent : Mix(Mix(palette.Surface, palette.Hover, hover), palette.Tint, selected);
        if (item.Enabled && press > 0) fill = Mix(fill, item.Name == "editorApply" ? palette.AccentText : palette.Accent, press * .12f);
        int height = item is ToolStripOverflowButton ? Math.Min(item.Height - 3, (int)Math.Round(36 * (e.ToolStrip?.DeviceDpi ?? 96) / 96f)) : item.Height - 3;
        Rectangle rect = new(1, (item.Height - height) / 2 + (int)Math.Round(press), item.Width - 3, height);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = CaptureDrawing.Rounded(rect, 7 * (e.ToolStrip?.DeviceDpi ?? 96) / 96f);
        using SolidBrush brush = new(fill);
        e.Graphics.FillPath(brush, path);
        if (item.Selected || (item is ToolStripButton { Checked: true }))
        {
            bool menuFocus = item.Selected && (e.ToolStrip?.IsDropDown == true || item is ToolStripOverflowButton);
            using Pen pen = new(menuFocus ? palette.Accent : Mix(palette.Border, palette.Accent, selected));
            e.Graphics.DrawPath(pen, path);
        }
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Foreground(e.Item);
        base.OnRenderItemText(e);
    }
    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Item.Tag is ShapeType || e.Item.Name is "editorApply" or "editorSave" or "editorSaveAs" or "editorCopy" or "editorStyle" or "editorEditMenu")
        {
            Color ink = Mix(palette.Surface, Foreground(e.Item), entrance.Value);
            EditorIcons.Draw(e.Graphics, e.ImageRectangle, e.Item.Tag as ShapeType?, e.Item.Name ?? "", ink);
        }
        else base.OnRenderItemImage(e); // Color swatches remain actual annotation colors.
    }
    private static Color Mix(Color a, Color b, float value) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * value), (int)(a.G + (b.G - a.G) * value), (int)(a.B + (b.B - a.B) * value));
}
