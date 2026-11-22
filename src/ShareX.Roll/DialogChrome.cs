// GPL-3.0-or-later. Compact controls shared by the editor's dialogs.
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ShareX.HelpersLib;
using TextDrawingInputBox = ShareX.ScreenCaptureLib.TextDrawingInputBox;

namespace ShareX.Roll;

internal static class DialogChrome
{
    internal static void ApplyFrame(Form form, CapturePalette palette, bool transparency, bool frameMaterialOnly = false)
    {
        if (form.Text.StartsWith("ShareX", StringComparison.Ordinal)) form.Text = "SnapFlow" + form.Text[6..];
        form.BackColor = palette.Background;
        form.ForeColor = palette.Text;
        form.Icon = ShareXResources.Icon;
        form.StartPosition = FormStartPosition.CenterParent;
        form.ShowInTaskbar = false;
        bool clientMaterial = !frameMaterialOnly && !palette.IsWinUI && !palette.IsMetro;
        WindowBackdrop? backdrop = clientMaterial ? new(form) : null;
        void Apply()
        {
            NativeMethods.UseImmersiveDarkMode(form.Handle, palette.Background.GetBrightness() < .5);
            if (palette.HasShapedControls)
                ApplyCaptionColors(form.Handle, palette);
            if (palette.IsMetro) ApplySquareCorners(form.Handle, square: true);
            if (backdrop != null) backdrop.Apply(palette, transparency);
            else if (transparency && !palette.IsWinUI && WindowBackdrop.IsSupported())
            {
                // These dialogs paint their own client area and use the system material in the frame.
                int material = 3; // DWMSBT_TRANSIENTWINDOW
                NativeMethods.DwmSetWindowAttribute(form.Handle, 38, ref material, sizeof(int));
            }
        }
        form.HandleCreated += (_, _) => Apply();
        if (form.IsHandleCreated) Apply();
    }

    internal static void ApplySquareCorners(IntPtr handle, bool square)
    {
        int preference = square ? 1 : 0; // DWMWCP_DONOTROUND / DWMWCP_DEFAULT
        NativeMethods.DwmSetWindowAttribute(handle, (int)DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    internal static void ApplyCaptionColors(IntPtr handle, CapturePalette? palette)
    {
        int caption = palette == null ? -1 : ColorTranslator.ToWin32(palette.Surface);
        int text = palette == null ? -1 : ColorTranslator.ToWin32(palette.Text);
        NativeMethods.DwmSetWindowAttribute(handle, (int)DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(handle, (int)DWMWINDOWATTRIBUTE.DWMWA_TEXT_COLOR, ref text, sizeof(int));
    }

    internal static void StyleButton(ButtonBase button, CapturePalette palette, bool primary = false, bool tile = false, bool quiet = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
        if (palette.HasShapedControls && button is MenuButton command)
        {
            command.PaintMenuArrow = (e, bounds) => ThemeIcons.TryDraw(e.Graphics,
                ThemeIcons.Centered(bounds.X + bounds.Width / 2f, button.Height / 2f, 13 * button.DeviceDpi / 96f),
                palette.Style, "chevronDown", button.Enabled ? palette.Text : palette.Secondary);
        }
        if (button is ColorButton color)
        {
            if (!color.ManualButtonClick)
            {
                color.ManualButtonClick = true;
                color.Click += (_, _) => color.ShowColorDialog();
            }
            color.BorderColor = palette.FieldBorder.IsEmpty ? palette.Secondary : palette.FieldBorder;
            void CenterSwatch() => color.Offset = Math.Max(1, (color.ClientSize.Height - 16 * color.DeviceDpi / 96) / 2);
            color.SizeChanged += (_, _) => CenterSwatch();
            CenterSwatch();
        }
        bool hover = false, pressed = false;
        button.MouseEnter += (_, _) => { hover = true; button.Invalidate(); };
        button.MouseLeave += (_, _) => { hover = pressed = false; button.Invalidate(); };
        button.MouseDown += (_, e) => { pressed = e.Button == MouseButtons.Left; button.Invalidate(); };
        button.MouseUp += (_, _) => { pressed = false; button.Invalidate(); };
        button.MouseCaptureChanged += (_, _) => { if (!button.Capture) { pressed = false; button.Invalidate(); } };
        button.GotFocus += (_, _) => button.Invalidate();
        button.LostFocus += (_, _) => button.Invalidate();
        button.ChangeUICues += (_, _) => button.Invalidate();
        if (button is CheckBox toggle) toggle.CheckedChanged += (_, _) => toggle.Invalidate();
        bool materialToggle = palette.IsMaterial && quiet && button is CheckBox;
        float buttonRadius = palette.CornerRadius(palette.IsWinUI ? CapturePalette.WinUIControlRadius :
            materialToggle ? 8 : palette.IsMaterial ? MaterialPalette.PillRadius : 7);
        if (palette.HasShapedControls)
            ButtonPointerBounds.ApplyInset(button, true, buttonRadius);
        button.Paint += (_, e) =>
        {
            Graphics g = e.Graphics;
            g.Clear(palette.Background);
            float s = button.DeviceDpi / 96f;
            RectangleF face = new(s, s, button.Width - 2 * s, button.Height - 2 * s);
            bool checkedState = button is CheckBox { Checked: true };
            float radius = palette.IsMaterial && !(materialToggle && checkedState) ? face.Height / 2 : buttonRadius * s;
            if (palette.HasShapedControls) CaptureDrawing.InsetAntialiasedFace(ref face, ref radius);
            if (face.Width <= 0 || face.Height <= 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool underlined = palette.IsClaude && button.Text.Length > 0 && !tile;
            bool textAction = quiet || underlined;
            bool focus = HasKeyboardFocus(button);
            Color ink = !button.Enabled ? palette.Secondary : primary && !palette.IsClaude ? palette.AccentText :
                checkedState || primary || (hover && textAction) ? palette.Accent : quiet ? palette.Secondary : palette.Text;
            Color fill = primary && !palette.IsClaude
                ? CaptureDrawing.Blend(palette.Accent, palette.Background, pressed ? .24f : hover ? .14f : 0)
                : checkedState || pressed ? palette.Tint : hover ? palette.Hover : palette.Surface;
            if (palette.UsesFilledActions)
            {
                ink = !button.Enabled ? palette.Secondary : primary ? palette.AccentText :
                    checkedState || pressed ? palette.TintText :
                    quiet && button.Text.Length > 0 ? palette.Accent : palette.Text;
                fill = !button.Enabled ? palette.Surface :
                    primary ? palette.PrimaryFill(hover ? 1 : 0, pressed ? 1 : 0) :
                    checkedState || pressed ? palette.Tint : hover ? palette.Hover : palette.RaisedSurface;
                if (palette.IsWinUI && button.Enabled && !primary && hover && !pressed)
                    fill = palette.WinUIHoverFill(checkedState ? palette.Tint : palette.RaisedSurface);
            }
            if (!textAction || palette.UsesFilledActions && (checkedState || button.Enabled && (hover || pressed)))
            {
                using var outline = CaptureDrawing.Rounded(face, radius);
                using SolidBrush brush = new(fill);
                g.FillPath(brush, outline);
            }
            if (focus || !palette.IsMaterial && (checkedState || !primary && !textAction))
                CaptureDrawing.DrawInsideBorder(g, face, radius,
                    focus && primary && palette.UsesFilledActions ? palette.AccentText : focus || checkedState ? palette.Accent : palette.Border,
                    focus ? 1.5f * s : s);
            Rectangle label = button.ClientRectangle;
            TextFormatFlags labelFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            if (button.Image is { } image)
            {
                int size = (int)Math.Round((tile ? 20 : 16) * s);
                Rectangle icon = new((button.Width - size) / 2, (button.Height - size) / 2, size, size);
                if (tile)
                {
                    icon.X = (int)Math.Round(12 * s);
                    if (!ThemeIcons.TryDraw(g, icon, palette.Style, ButtonAction(button), ink))
                        g.DrawImage(image, icon);
                    label.X += (int)Math.Round(44 * s);
                    label.Width -= label.X + (int)Math.Round(10 * s);
                    labelFlags &= ~TextFormatFlags.HorizontalCenter;
                }
                else
                {
                    if (button.Text.Length > 0)
                    {
                        int gap = (int)Math.Round(5 * s);
                        int width = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, labelFlags).Width;
                        int group = Math.Min(button.Width, size + gap + width);
                        icon.X = (button.Width - group) / 2;
                        label.X = icon.Right + gap;
                        label.Width = Math.Max(0, group - size - gap);
                    }
                    if (!ThemeIcons.TryDraw(g, icon, palette.Style, ButtonAction(button), ink))
                        DrawButtonIcon(button.Name, g, icon, image, ink);
                }
            }
            if (underlined)
                CaptureDrawing.DrawUnderlinedText(g, button.Text, button.Font, label, ink,
                    primary || hover ? palette.Accent : palette.Secondary, labelFlags, s, hover ? 1 : 0);
            else if (button.Text.Length > 0)
                TextRenderer.DrawText(g, button.Text, button.Font, label, ink, labelFlags);
        };
    }

    internal static bool HasKeyboardFocus(Control control) =>
        control.Focused && (NativeMethods.SendMessage(control.Handle, (uint)WindowsMessages.QUERYUISTATE, IntPtr.Zero, IntPtr.Zero).ToInt64() & 1) == 0;

    internal static void WrapNumber(NumericUpDown number, Rectangle bounds, CapturePalette palette)
    {
        Control parent = number.Parent!;
        int index = parent.Controls.GetChildIndex(number);
        AnchorStyles anchor = number.Anchor;
        number.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        number.TextAlign = HorizontalAlignment.Center;
        SettingsNumberField field = new(number);
        parent.Controls.Add(field);
        parent.Controls.SetChildIndex(field, index);
        field.Bounds = bounds;
        field.Anchor = anchor;
        field.TabIndex = number.TabIndex;
        field.ApplyPalette(palette);
    }

    internal static void WrapText(TextBox edit, Rectangle bounds, CapturePalette palette)
    {
        Control parent = edit.Parent!;
        int index = parent.Controls.GetChildIndex(edit);
        AnchorStyles anchor = edit.Anchor;
        Control field = CreateTextField(edit, palette);
        parent.Controls.Add(field);
        parent.Controls.SetChildIndex(field, index);
        field.Bounds = bounds;
        field.Anchor = anchor;
        field.TabIndex = edit.TabIndex;
    }

    internal static Control CreateTextField(TextBox edit, CapturePalette palette) => new DialogTextField(edit, palette);

    private sealed class DialogTextField : Control
    {
        private readonly TextBox edit;
        private readonly CapturePalette palette;

        internal DialogTextField(TextBox edit, CapturePalette palette)
        {
            this.edit = edit;
            this.palette = palette;
            TabStop = false;
            BackColor = palette.Background;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            edit.BorderStyle = BorderStyle.None;
            edit.AutoSize = false;
            edit.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            edit.BackColor = palette.Canvas;
            edit.ForeColor = palette.Text;
            edit.GotFocus += (_, _) => Invalidate();
            edit.LostFocus += (_, _) => Invalidate();
            Controls.Add(edit);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (edit == null) return;
            int inset = (int)Math.Round(7 * DeviceDpi / 96f);
            edit.Bounds = new(inset, Math.Max(0, (Height - edit.PreferredHeight) / 2),
                Math.Max(1, Width - 2 * inset), edit.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = DeviceDpi / 96f;
            float radius = palette.CornerRadius(palette.IsWinUI ? CapturePalette.WinUIControlRadius : palette.IsMaterial ? MaterialPalette.FieldRadius : 6) * scale;
            using var face = CaptureDrawing.Rounded(ClientRectangle, radius);
            using SolidBrush brush = new(palette.Canvas);
            e.Graphics.FillPath(brush, face);
            Color border = edit.Focused ? palette.Accent : palette.FieldBorder.IsEmpty ? palette.Border : palette.FieldBorder;
            CaptureDrawing.DrawInsideBorder(e.Graphics, ClientRectangle, radius, border, (edit.Focused && palette.IsMaterial ? 1.5f : 1) * scale);
        }
    }

    private static string? ButtonAction(ButtonBase button) => button.Name switch
    {
        "btnScreenColorPicker" => "eyedropper",
        "btnClipboardColorPicker" => "clipboard",
        "btnClipboardStatus" => "ok",
        "btnSwapEnterKey" => "enter",
        "btnGradient" => "gradient",
        "cbBold" => "bold",
        "cbItalic" => "italic",
        "cbUnderline" => "underline",
        "btnCenter" => "canvasCenter",
        "btnCanvasExpandDown" => "canvasDown",
        "btnCanvasExpandRight" => "canvasRight",
        "btnAlignmentHorizontal" when button.FindForm() is TextDrawingInputBox horizontal => horizontal.Options.AlignmentHorizontal switch
        {
            StringAlignment.Near => "alignLeft",
            StringAlignment.Far => "alignRight",
            _ => "alignCenter",
        },
        "btnAlignmentVertical" when button.FindForm() is TextDrawingInputBox vertical => vertical.Options.AlignmentVertical switch
        {
            StringAlignment.Near => "alignTop",
            StringAlignment.Far => "alignBottom",
            _ => "alignMiddle",
        },
        _ => null,
    };

    private static void DrawButtonIcon(string name, Graphics graphics, Rectangle bounds, Image image, Color ink)
    {
        if (name is "btnScreenColorPicker" or "btnClipboardColorPicker")
        {
            var state = graphics.Save();
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 16f, bounds.Height / 16f);
            using Pen pen = new(ink, 1.35f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            if (name == "btnClipboardColorPicker")
            {
                graphics.DrawRectangle(pen, 3, 3, 10, 12);
                using SolidBrush clip = new(ink);
                graphics.FillRectangle(clip, 5, 1, 6, 4);
                graphics.DrawLine(pen, 6, 9, 10, 9);
                graphics.DrawLine(pen, 6, 12, 10, 12);
            }
            else
            {
                graphics.DrawLines(pen, [new Point(2, 14), new(3, 10), new(10, 3), new(13, 6), new(6, 13), new(2, 14)]);
                graphics.DrawLine(pen, 8, 3, 13, 8);
                graphics.DrawLine(pen, 10, 3, 12, 1);
                graphics.DrawLine(pen, 13, 6, 15, 4);
                graphics.DrawLine(pen, 12, 1, 15, 4);
            }
            graphics.Restore(state);
            return;
        }
        (string letter, FontStyle style) = name switch
        {
            "cbBold" => ("B", FontStyle.Bold),
            "cbItalic" => ("I", FontStyle.Italic),
            "cbUnderline" => ("U", FontStyle.Underline),
            _ => (string.Empty, FontStyle.Regular)
        };
        if (letter.Length > 0)
        {
            using Font glyph = new(FontFamily.GenericSansSerif, bounds.Height * .84f, style, GraphicsUnit.Pixel);
            TextRenderer.DrawText(graphics, letter, glyph, bounds, ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return;
        }
        if (name == "btnSwapEnterKey" || name == "btnGradient")
        {
            using Pen pen = new(ink, Math.Max(1, bounds.Width / 11f))
            {
                StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
            };
            if (name == "btnSwapEnterKey")
            {
                float s = bounds.Width / 16f, x = bounds.Left, y = bounds.Top;
                graphics.DrawLines(pen, [new PointF(x + 14 * s, y + 3 * s), new(x + 14 * s, y + 9 * s), new(x + 2 * s, y + 9 * s)]);
                graphics.DrawLines(pen, [new PointF(x + 6 * s, y + 5 * s), new(x + 2 * s, y + 9 * s), new(x + 6 * s, y + 13 * s)]);
            }
            else
            {
                bounds.Inflate(-2, -2);
                using LinearGradientBrush gradient = new(bounds, Color.FromArgb(20, ink), ink, LinearGradientMode.Horizontal);
                graphics.FillRectangle(gradient, bounds);
                graphics.DrawRectangle(pen, bounds);
            }
            return;
        }
        using ImageAttributes tint = new();
        tint.SetColorMatrix(new ColorMatrix { Matrix00 = 0, Matrix11 = 0, Matrix22 = 0,
            Matrix40 = ink.R / 255f, Matrix41 = ink.G / 255f, Matrix42 = ink.B / 255f });
        graphics.DrawImage(image, bounds, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, tint);
    }

    internal static void StyleMenu(ToolStripDropDown menu, CapturePalette palette, Font font)
    {
        menu.BackColor = palette.UsesFilledActions ? palette.RaisedSurface : palette.Surface;
        menu.ForeColor = palette.Text;
        menu.Font = font;
        menu.Renderer = new MenuRenderer(palette) { RoundedEdges = false };
        if (palette.UsesFilledActions)
        {
            menu.DropShadowEnabled = false;
            _ = new EditorMenuBackdrop(menu, palette, transparency: false);
        }
        foreach (ToolStripItem item in menu.Items)
        {
            item.ForeColor = palette.Text;
            if (item is ToolStripDropDownItem { HasDropDownItems: true } nested)
                StyleMenu(nested.DropDown, palette, font);
        }
    }

    private sealed class MenuRenderer(CapturePalette palette) : ToolStripProfessionalRenderer(new MenuColors(palette))
    {
        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            string? action = e.Item.Name switch
            {
                "tsmiAlignmentLeft" => "alignLeft",
                "tsmiAlignmentCenter" => "alignCenter",
                "tsmiAlignmentRight" => "alignRight",
                "tsmiAlignmentTop" => "alignTop",
                "tsmiAlignmentMiddle" => "alignMiddle",
                "tsmiAlignmentBottom" => "alignBottom",
                _ => null,
            };
            if (!ThemeIcons.TryDraw(e.Graphics, e.ImageRectangle, palette.Style, action, e.Item.Enabled ? palette.Text : palette.Secondary))
                base.OnRenderItemImage(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            if (!ThemeIcons.TryDraw(e.Graphics, e.ImageRectangle, palette.Style, "ok", e.Item.Enabled ? palette.Text : palette.Secondary))
                base.OnRenderItemCheck(e);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (palette.UsesFilledActions)
            {
                bool selected = e.Item.Selected || e.Item is ToolStripMenuItem { Checked: true };
                e.TextColor = !e.Item.Enabled ? palette.Secondary : selected && palette.IsMaterial ? palette.TintText : palette.Text;
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, e.TextColor, e.TextFormat);
            }
            else base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!palette.UsesFilledActions) { base.OnRenderMenuItemBackground(e); return; }
            bool isChecked = e.Item is ToolStripMenuItem { Checked: true };
            bool highlight = e.Item.Selected && (!palette.IsWinUI || e.Item.Enabled);
            if (!highlight && !isChecked) return;
            float scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
            RectangleF face = new(2 * scale, scale, e.Item.Width - 4 * scale, e.Item.Height - 2 * scale);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var shape = CaptureDrawing.Rounded(face,
                palette.CornerRadius(palette.IsWinUI ? CapturePalette.WinUIControlRadius * scale : face.Height / 2));
            Color fillColor = palette.Tint;
            if (palette.IsWinUI)
            {
                Color resting = isChecked ? palette.Tint : palette.RaisedSurface;
                fillColor = highlight ? palette.WinUIHoverFill(resting) : resting;
            }
            using SolidBrush fill = new(fillColor);
            e.Graphics.FillPath(fill, shape);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (!palette.UsesFilledActions) { base.OnRenderToolStripBorder(e); return; }
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = e.ToolStrip.DeviceDpi / 96f;
            CaptureDrawing.DrawInsideBorder(e.Graphics, e.ToolStrip.ClientRectangle, palette.MenuRadius * scale, palette.Border, scale);
            }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item is { Enabled: false } ? palette.Secondary : palette.Text;
            Rectangle bounds = e.ArrowRectangle;
            float size = 12 * (e.Item?.GetCurrentParent()?.DeviceDpi ?? 96) / 96f;
            string action = e.Direction switch
            {
                ArrowDirection.Left => "chevronLeft",
                ArrowDirection.Right => "chevronRight",
                ArrowDirection.Up => "chevronUp",
                _ => "chevronDown",
            };
            if (ThemeIcons.TryDraw(e.Graphics, ThemeIcons.Centered(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f, size),
                palette.Style, action, e.ArrowColor)) return;
            base.OnRenderArrow(e);
        }
    }

    private sealed class MenuColors(CapturePalette palette) : ProfessionalColorTable
    {
        private Color Surface => palette.UsesFilledActions ? palette.RaisedSurface : palette.Surface;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => palette.Border;
        public override Color MenuItemSelected => palette.Hover;
        public override Color MenuItemBorder => palette.Border;
        public override Color CheckBackground => palette.Tint;
        public override Color CheckSelectedBackground => palette.Tint;
        public override Color CheckPressedBackground => palette.Tint;
    }
}
