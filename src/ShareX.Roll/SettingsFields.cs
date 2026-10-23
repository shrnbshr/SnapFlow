// GPL-3.0-or-later. Rounded settings chrome around native WinForms input semantics.
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ShareX.Roll;

internal sealed class SettingsNumericInput : NumericUpDown
{
    private bool arranging, clipping;
    private Rectangle editClip;
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is TextBox edit)
        {
            edit.SizeChanged += (_, _) => ClipToEdit();
            edit.LocationChanged += (_, _) => ClipToEdit();
        }
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); editClip = Rectangle.Empty; ClipToEdit(); }

    private void ClipToEdit()
    {
        if (clipping || !IsHandleCreated) return;
        clipping = true;
        try
        {
            for (int pass = 0; pass < 2; pass++)
            {
                TextBox? edit = Controls.OfType<TextBox>().FirstOrDefault();
                if (edit == null) return;
                Rectangle bounds = Rectangle.Intersect(edit.Bounds, ClientRectangle);
                if (bounds.IsEmpty || bounds == editClip) return;
                editClip = bounds;
                Region? previous = Region;
                Region = new Region(bounds);
                previous?.Dispose();
            }
        }
        finally { clipping = false; }
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        if (arranging) return;
        arranging = true;
        try
        {
            base.OnLayout(e);
            // UpDownBase positions its internal edit after raising Layout. Apply our
            // borderless edit bounds after that native layout, including at high DPI.
            foreach (Control child in Controls)
            {
                if (child is TextBox edit) { edit.AutoSize = false; edit.Bounds = ClientRectangle; }
                else child.Visible = false;
            }
            ClipToEdit();
        }
        finally { arranging = false; }
    }
}

internal sealed class SettingsNumberField : Control
{
    private readonly NumericUpDown number;
    private readonly System.Windows.Forms.Timer repeat = new() { Interval = 400 };
    private CapturePalette palette = CapturePalette.Light;
    private int pressed;
    private bool hovering;

    internal SettingsNumberField(NumericUpDown number)
    {
        this.number = number;
        Name = number.Name + "Field";
        Size = new Size(120, 34);
        TabStop = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        number.BorderStyle = BorderStyle.None;
        number.Margin = Padding.Empty;
        Controls.Add(number);
        number.HandleCreated += (_, _) => PerformLayout();
        number.GotFocus += (_, _) => Invalidate();
        number.LostFocus += (_, _) => Invalidate();
        repeat.Tick += (_, _) => { repeat.Interval = 80; Step(); };
    }

    internal void ApplyPalette(CapturePalette value)
    {
        palette = value;
        BackColor = Parent?.BackColor ?? value.Surface;
        number.BackColor = value.Canvas;
        number.ForeColor = value.Text;
        foreach (Control child in number.Controls)
        {
            child.BackColor = value.Canvas;
            child.ForeColor = value.Text;
            if (child is TextBox edit) edit.BorderStyle = BorderStyle.None;
        }
        ArrangeEdit();
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (number == null) return;
        int inset = Scale(10), stepper = Scale(28);
        number.Bounds = new Rectangle(inset, (Height - number.PreferredHeight) / 2,
            Math.Max(1, Width - inset - stepper - Scale(5)), number.PreferredHeight);
        ArrangeEdit();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); PerformLayout(); Invalidate(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); PerformLayout(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (number != null) PerformLayout(); }

    private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96f);
    private void ArrangeEdit()
    {
        // Keep the native edit, validation, selection, clipboard, and arrow-key handling.
        // Only its square spin-button window is replaced by the rounded outer hit region.
        foreach (Control child in number.Controls)
        {
            if (child is TextBox edit)
            {
                edit.AutoSize = false;
                edit.Bounds = new Rectangle(0, 0, number.ClientSize.Width, number.ClientSize.Height);
            }
            else child.Visible = false;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        float s = DeviceDpi / 96f;
        using (SolidBrush background = new(Parent?.BackColor ?? palette.Surface)) g.FillRectangle(background, ClientRectangle);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath shape = CaptureDrawing.Rounded(new RectangleF(s, s, Width - 2 * s, Height - 2 * s), 10 * s);
        using SolidBrush fill = new(palette.Canvas);
        using Pen edge = new(number.ContainsFocus ? palette.Accent : palette.Border, (number.ContainsFocus ? 1.5f : 1) * s);
        g.FillPath(fill, shape);
        g.DrawPath(edge, shape);
        float x = Width - 15 * s;
        using Pen arrow = new(hovering || pressed != 0 ? palette.Text : palette.Secondary, 1.4f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float top = Height / 2f - 5 * s, bottom = Height / 2f + 5 * s;
        g.DrawLines(arrow, new PointF[] { new(x - 3 * s, top + 1.5f * s), new(x, top - 1.5f * s), new(x + 3 * s, top + 1.5f * s) });
        g.DrawLines(arrow, new PointF[] { new(x - 3 * s, bottom - 1.5f * s), new(x, bottom + 1.5f * s), new(x + 3 * s, bottom - 1.5f * s) });
    }

    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); bool next = e.X >= Width - Scale(28); if (next != hovering) { hovering = next; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        number.Focus();
        if (e.X < Width - Scale(28)) return;
        pressed = e.Y < Height / 2 ? 1 : -1;
        Capture = true;
        Step();
        repeat.Interval = 400;
        repeat.Start();
        Invalidate();
    }
    private void Step() { if (pressed > 0) number.UpButton(); else if (pressed < 0) number.DownButton(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) Capture = false; }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); pressed = 0; repeat.Stop(); Invalidate(); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible && repeat != null) { repeat.Stop(); pressed = 0; } }
    protected override void Dispose(bool disposing) { if (disposing) repeat.Dispose(); base.Dispose(disposing); }
}

internal sealed class SettingsChoice : ComboBox
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PaintData
    {
        public IntPtr Hdc;
        public int Erase, Left, Top, Right, Bottom, Restore, Update;
        public int Reserved0, Reserved1, Reserved2, Reserved3, Reserved4, Reserved5, Reserved6, Reserved7;
    }
    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr window, out PaintData data);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(IntPtr window, ref PaintData data);
    private CapturePalette palette = CapturePalette.Light;
    private ToolStripDropDownMenu? menu;
    private UiTween? menuReveal;
    private bool hovering;
    internal bool MenuAnimating => menuReveal?.IsRunning == true;

    internal SettingsChoice()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = (int)Math.Round(26 * DeviceDpi / 96f);
        Cursor = Cursors.Hand;
    }

    internal void ApplyPalette(CapturePalette value)
    {
        CloseMenu();
        palette = value;
        BackColor = value.Canvas;
        ForeColor = value.Text;
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ItemHeight = (int)Math.Round(26 * DeviceDpi / 96f);
        Parent?.PerformLayout();
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ItemHeight = (int)Math.Round(26 * DeviceDpi / 96f);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg is 0x201 or 0x203) { Focus(); ToggleMenu(); return; }
        if (message.Msg == 0x14F) { if (message.WParam != IntPtr.Zero) ShowMenu(); else CloseMenu(); return; }
        if (message.Msg == 0x157) { message.Result = menu?.Visible == true ? (IntPtr)1 : IntPtr.Zero; return; }
        if (message.Msg == 0xF && IsHandleCreated)
        {
            // Do not let the native combo draw a blank text slot / white arrow first.
            // Commit the complete rounded field in one buffered WM_PAINT operation.
            IntPtr dc = BeginPaint(Handle, out PaintData paint);
            try
            {
                using Graphics target = Graphics.FromHdc(dc);
                using BufferedGraphics buffer = BufferedGraphicsManager.Current.Allocate(target, ClientRectangle);
                PaintField(buffer.Graphics);
                buffer.Render(dc);
            }
            finally { EndPaint(Handle, ref paint); }
            message.Result = IntPtr.Zero;
            return;
        }
        if (message.Msg == 0x14) { message.Result = (IntPtr)1; return; }
        if (message.Msg is 0x317 or 0x318 && message.WParam != IntPtr.Zero)
        {
            using Graphics graphics = Graphics.FromHdc(message.WParam);
            Rectangle visible = ClientRectangle;
            for (Control? ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
                visible.Intersect(new Rectangle(PointToClient(ancestor.PointToScreen(Point.Empty)), ancestor.ClientSize));
            if (!visible.IsEmpty)
            {
                GraphicsState state = graphics.Save();
                graphics.SetClip(visible, CombineMode.Intersect);
                PaintField(graphics);
                graphics.Restore(state);
            }
            message.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref message);
    }

    private void PaintField(Graphics g)
    {
        float s = DeviceDpi / 96f;
        using (SolidBrush background = new(Parent?.BackColor ?? palette.Surface)) g.FillRectangle(background, ClientRectangle);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath shape = CaptureDrawing.Rounded(new RectangleF(s, s, Width - 2 * s, Height - 2 * s), 8 * s);
        using SolidBrush fill = new(hovering ? palette.Hover : palette.Canvas);
        using Pen edge = new(Focused || menu?.Visible == true ? palette.Accent : palette.Border, (Focused ? 1.5f : 1) * s);
        g.FillPath(fill, shape);
        g.DrawPath(edge, shape);
        TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(10 * s), 0, Math.Max(0, Width - (int)(36 * s)), Height), Enabled ? palette.Text : palette.Secondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform | UiLanguage.TextDirection);
        float x = Width - 15 * s, y = Height / 2f;
        using Pen arrow = new(palette.Secondary, 1.5f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(arrow, new PointF[] { new(x - 3 * s, y - 1.5f * s), new(x, y + 1.5f * s), new(x + 3 * s, y - 1.5f * s) });
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.F4 or (Keys.Alt | Keys.Down) or Keys.Space) { ToggleMenu(); return true; }
        if (keyData == Keys.Escape && menu?.Visible == true) { CloseMenu(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ToggleMenu() { if (menu?.Visible == true) CloseMenu(); else ShowMenu(); }
    private void ShowMenu()
    {
        if (!Enabled || Items.Count == 0 || menu?.Visible == true) return;
        CloseMenu();
        ToolStripDropDownMenu popup = new()
        {
            Name = Name + "Menu", Font = Font, ShowImageMargin = false, ShowCheckMargin = true,
            AutoSize = true, Padding = new Padding(5), BackColor = palette.Surface, ForeColor = palette.Text,
            MaximumSize = new Size(Math.Max(Width, DropDownWidth), (int)(360 * DeviceDpi / 96f)),
            Renderer = new ChoiceMenuRenderer(palette), Opacity = UiMotion.Enabled ? .01 : 1
        };
        menu = popup;
        ToolStripMenuItem? selected = null;
        for (int index = 0; index < Items.Count; index++)
        {
            int captured = index;
            ToolStripMenuItem item = new(GetItemText(Items[index]))
            {
                Checked = index == SelectedIndex, ForeColor = palette.Text, BackColor = palette.Surface,
                AutoSize = false, Size = new Size(Math.Max(Width, DropDownWidth) - 12, (int)(32 * DeviceDpi / 96f)),
                AccessibleName = GetItemText(Items[index]),
                RightToLeft = (Items[index] is LanguageOption { Mode: not LanguageMode.System } option
                    ? System.Globalization.CultureInfo.GetCultureInfo(option.CultureName).TextInfo.IsRightToLeft
                    : UiLanguage.Culture.TextInfo.IsRightToLeft) ? RightToLeft.Yes : RightToLeft.No
            };
            item.Click += (_, _) => { SelectedIndex = captured; Focus(); };
            popup.Items.Add(item);
            if (item.Checked) selected = item;
        }
        popup.Closed += (_, _) => { menuReveal?.Dispose(); menuReveal = null; Invalidate(); };
        popup.SizeChanged += (_, _) =>
        {
            using GraphicsPath outline = CaptureDrawing.Rounded(popup.ClientRectangle, 10 * DeviceDpi / 96f);
            Region? previous = popup.Region;
            popup.Region = new Region(outline);
            previous?.Dispose();
        };
        menuReveal = new UiTween(popup, () => { if (!popup.IsDisposed) popup.Opacity = menuReveal == null ? 1 : Math.Max(.01, menuReveal.Value); });
        popup.Show(this, new Point(0, Height + (int)(4 * DeviceDpi / 96f)));
        selected?.Select();
        menuReveal.AnimateTo(1, 140);
        Invalidate();
    }

    internal void CloseMenu()
    {
        ToolStripDropDownMenu? previous = menu;
        menu = null;
        menuReveal?.Dispose();
        menuReveal = null;
        previous?.Close();
        previous?.Dispose();
        Invalidate();
    }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) CloseMenu(); }
    protected override void Dispose(bool disposing) { if (disposing) CloseMenu(); base.Dispose(disposing); }

    private sealed class ChoiceMenuRenderer(CapturePalette palette) : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(palette.Surface); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath shape = CaptureDrawing.Rounded(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 10 * e.ToolStrip.DeviceDpi / 96f);
            using Pen edge = new(palette.Border);
            e.Graphics.DrawPath(edge, shape);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            using SolidBrush fill = new(e.Item.Selected ? palette.Hover : palette.Surface);
            using GraphicsPath shape = CaptureDrawing.Rounded(new RectangleF(0, 0, e.Item.Width, e.Item.Height), 6 * (e.ToolStrip?.DeviceDpi ?? 96) / 96f);
            e.Graphics.FillPath(fill, shape);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = palette.Text; base.OnRenderItemText(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle box = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using Pen pen = new(palette.Accent, 1.7f * (e.ToolStrip?.DeviceDpi ?? 96) / 96f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawLines(pen, new PointF[] { new(box.Left + box.Width * .2f, box.Top + box.Height * .5f), new(box.Left + box.Width * .43f, box.Top + box.Height * .72f), new(box.Left + box.Width * .8f, box.Top + box.Height * .27f) });
        }
    }
}
