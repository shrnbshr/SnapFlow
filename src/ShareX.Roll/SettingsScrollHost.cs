// GPL-3.0-or-later. Settings presentation; WinForms still owns scrolling and focus navigation.
using System.Drawing.Drawing2D;
using static ShareX.Roll.Localization.UiText;

namespace ShareX.Roll;

internal sealed class SettingsScrollHost : Panel
{
    private readonly Panel clip = new() { Margin = Padding.Empty, TabStop = false };
    private readonly Panel viewport;
    private readonly SettingsScrollBar bar;
    private bool arranging;

    internal SettingsScrollHost(Panel viewport)
    {
        this.viewport = viewport;
        Name = "settingsScrollHost";
        TabStop = false;
        bar = new SettingsScrollBar(viewport) { Name = "settingsScrollBar", AccessibleName = L("Settings") };
        clip.Controls.Add(viewport);
        Controls.Add(clip);
        Controls.Add(bar);
        viewport.Layout += (_, _) => Arrange();
        viewport.Scroll += (_, _) => bar.Invalidate();
        viewport.ControlAdded += (_, e) =>
        {
            // AutoScrollPosition and focus scrolling also move the content without raising Scroll.
            if (e.Control != null) e.Control.LocationChanged += (_, _) => bar.Invalidate();
        };
    }

    internal void ApplyPalette(CapturePalette palette)
    {
        BackColor = clip.BackColor = viewport.BackColor = bar.BackColor = palette.Background;
        bar.Palette = palette;
        bar.Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Arrange();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Arrange();
    }

    private void Arrange()
    {
        if (arranging || bar == null) return;
        arranging = true;
        try
        {
            int gutter = (int)Math.Ceiling(20 * DeviceDpi / 96f);
            // Keep the native scroll model, wheel settings, and ScrollControlIntoView. Clip only
            // the non-client scrollbar, rather than hiding it with Win32 calls during layout.
            for (int pass = 0; pass < 2; pass++)
            {
                bool overflowing = viewport.DisplayRectangle.Height > ClientSize.Height;
                clip.Bounds = new Rectangle(0, 0, Math.Max(1, ClientSize.Width - (overflowing ? gutter : 0)), ClientSize.Height);
                int nativeBar = viewport.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
                viewport.Bounds = new Rectangle(0, 0, clip.Width + nativeBar, clip.Height);
                nativeBar = viewport.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
                viewport.Width = clip.Width + nativeBar;
            }
            bar.Bounds = new Rectangle(clip.Right, 0, gutter, ClientSize.Height);
            bar.Visible = viewport.VerticalScroll.Visible;
            bar.Invalidate();
        }
        finally { arranging = false; }
    }

    private sealed class SettingsScrollBar : Control
    {
        private readonly Panel viewport;
        private bool hovering;
        private bool dragging;
        private int dragOffset;
        private int wheelRemainder;
        internal CapturePalette Palette { get; set; } = CapturePalette.Light;
        private int Offset => -viewport.AutoScrollPosition.Y;
        private int Maximum => Math.Max(0, viewport.DisplayRectangle.Height - viewport.ClientSize.Height);
        private int Scale(int value) => (int)Math.Ceiling(value * DeviceDpi / 96f);

        internal SettingsScrollBar(Panel viewport)
        {
            this.viewport = viewport;
            TabStop = true;
            AccessibleRole = AccessibleRole.ScrollBar;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        private Rectangle Track => new(0, Scale(4), Width, Math.Max(0, Height - Scale(8)));
        private Rectangle Thumb
        {
            get
            {
                Rectangle track = Track;
                int height = Math.Min(track.Height, Math.Max(Scale(32),
                    (int)((long)track.Height * viewport.ClientSize.Height / Math.Max(1, viewport.DisplayRectangle.Height))));
                int y = track.Top + (int)((long)(track.Height - height) * Offset / Math.Max(1, Maximum));
                return new Rectangle(0, y, Width, height);
            }
        }

        private void ScrollTo(int value)
        {
            viewport.AutoScrollPosition = new Point(0, Math.Clamp(value, 0, Maximum));
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle thumb = Thumb;
            if (Maximum == 0 || thumb.Height == 0) return;
            int width = Scale(hovering || dragging || Focused ? 8 : 6);
            RectangleF shape = new((Width - width) / 2f, thumb.Top, width, thumb.Height);
            Color ink = SystemInformation.HighContrast ? SystemColors.WindowText : dragging ? Palette.Text :
                hovering || Focused ? Palette.Secondary : Color.FromArgb(
                    (Palette.Background.R + 4 * Palette.Secondary.R) / 5,
                    (Palette.Background.G + 4 * Palette.Secondary.G) / 5,
                    (Palette.Background.B + 4 * Palette.Secondary.B) / 5);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = CaptureDrawing.Rounded(shape, width / 2f);
            using SolidBrush brush = new(ink);
            e.Graphics.FillPath(brush, path);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1), Palette.Text, BackColor);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || Maximum == 0) return;
            Focus();
            Rectangle thumb = Thumb;
            if (thumb.Contains(e.Location))
            {
                dragOffset = e.Y - thumb.Top;
                dragging = true;
                Capture = true;
                Invalidate();
            }
            else ScrollTo(Offset + (e.Y < thumb.Top ? -1 : 1) * Math.Max(1, viewport.ClientSize.Height - Scale(32)));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            int travel = Track.Height - Thumb.Height;
            if (travel > 0) ScrollTo((int)Math.Round((double)(e.Y - dragOffset - Track.Top) * Maximum / travel));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) Capture = false;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            dragging = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e is HandledMouseEventArgs handled) handled.Handled = true;
            wheelRemainder += e.Delta;
            int notches = wheelRemainder / SystemInformation.MouseWheelScrollDelta;
            wheelRemainder %= SystemInformation.MouseWheelScrollDelta;
            int lines = SystemInformation.MouseWheelScrollLines;
            int step = lines < 0 ? viewport.ClientSize.Height : lines * viewport.VerticalScroll.SmallChange;
            ScrollTo(Offset - notches * step);
        }

        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is
            Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int page = Math.Max(1, viewport.ClientSize.Height - Scale(32));
            int? target = e.KeyCode switch
            {
                Keys.Up => Offset - viewport.VerticalScroll.SmallChange,
                Keys.Down => Offset + viewport.VerticalScroll.SmallChange,
                Keys.PageUp => Offset - page,
                Keys.PageDown => Offset + page,
                Keys.Home => 0,
                Keys.End => Maximum,
                _ => null
            };
            if (target.HasValue) { ScrollTo(target.Value); e.Handled = e.SuppressKeyPress = true; }
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new ScrollBarAccessibleObject(this);

        private sealed class ScrollBarAccessibleObject(SettingsScrollBar owner) : ControlAccessibleObject(owner)
        {
            public override string? Value
            {
                get => owner.Maximum == 0 ? "0%" : $"{100L * owner.Offset / owner.Maximum}%";
                set
                {
                    if (int.TryParse(value?.TrimEnd('%'), out int percent))
                        owner.ScrollTo((int)((long)owner.Maximum * Math.Clamp(percent, 0, 100) / 100));
                }
            }
        }
    }
}
