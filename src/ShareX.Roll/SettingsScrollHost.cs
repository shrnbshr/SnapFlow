// GPL-3.0-or-later. Settings presentation; WinForms still owns scrolling and focus navigation.
using System.Drawing.Drawing2D;
using System.Diagnostics;
using static ShareX.Roll.Localization.UiText;

namespace ShareX.Roll;

internal sealed class SettingsScrollHost : Panel
{
    private readonly Panel clip = new() { Margin = Padding.Empty, TabStop = false };
    private readonly Panel viewport;
    private readonly SettingsScrollBar bar;
    private readonly System.Windows.Forms.Timer motion = new() { Interval = 15 };
    private readonly Stopwatch clock = new();
    private readonly HashSet<Control> observed = new();
    private double position, target, lastSample;
    private int lastWritten, lastMaximum;
    private bool applying;
    private bool arranging;
    internal bool IsScrolling => motion.Enabled;
    internal long ScrollUpdateTicks { get; private set; }
    internal long MaximumScrollUpdateTicks { get; private set; }
    internal int ScrollUpdates { get; private set; }
    private int Offset => -viewport.AutoScrollPosition.Y;
    private int Maximum => Math.Max(0, viewport.DisplayRectangle.Height - viewport.ClientSize.Height);

    internal SettingsScrollHost(Panel viewport)
    {
        this.viewport = viewport;
        Name = "settingsScrollHost";
        TabStop = false;
        bar = new SettingsScrollBar(this) { Name = "settingsScrollBar", AccessibleName = L("Settings") };
        if (viewport is SettingsScrollViewport smooth) smooth.WheelRequested = ScrollWheel;
        motion.Tick += (_, _) => Advance();
        clip.Controls.Add(viewport);
        Controls.Add(clip);
        Controls.Add(bar);
        viewport.Layout += (_, _) => Arrange();
        viewport.Scroll += (_, _) => ObservePosition();
        viewport.SizeChanged += (_, _) => StopMotion();
        VisibleChanged += (_, _) => { if (!Visible) StopMotion(); };
        HandleDestroyed += (_, _) => StopMotion();
        viewport.ControlAdded += (_, e) =>
        {
            // AutoScrollPosition and focus scrolling also move the content without raising Scroll.
            if (e.Control != null)
            {
                e.Control.LocationChanged += (_, _) => ObservePosition();
                ObserveInput(e.Control);
            }
        };
        foreach (Control child in viewport.Controls) ObserveInput(child);
    }

    private void ObserveInput(Control control)
    {
        if (!observed.Add(control)) return;
        // Clicking or editing an already visible field must stop movement too; it may not
        // cause ScrollControlIntoView or change the native scroll offset at all.
        control.MouseDown += (_, _) => StopMotion();
        control.KeyDown += (_, _) => StopMotion();
        control.GotFocus += (_, _) => StopMotion();
        control.ControlAdded += (_, e) => { if (e.Control != null) ObserveInput(e.Control); };
        control.Disposed += (_, _) => observed.Remove(control);
        foreach (Control child in control.Controls) ObserveInput(child);
    }

    private void ObservePosition()
    {
        // Focus navigation and accessibility can move AutoScroll without a Scroll event.
        if (!applying && Offset != lastWritten) StopMotion();
        bar.Invalidate();
    }

    internal void ScrollWheel(int delta)
    {
        if (delta == 0) return;
        if (!motion.Enabled && Offset != lastWritten) StopMotion();
        int lines = SystemInformation.MouseWheelScrollLines;
        if (lines == 0 || Maximum == 0) { StopMotion(); return; }
        double step = lines < 0 ? viewport.ClientSize.Height : (double)lines * Math.Max(viewport.Font.Height, viewport.VerticalScroll.SmallChange);
        double distance = -delta * step / SystemInformation.MouseWheelScrollDelta;
        // Small wheel deltas already represent fine-grained input, possibly with OS inertia.
        // Keep them direct and retain subpixel remainder instead of adding another animation.
        if (Math.Abs((long)delta) < SystemInformation.MouseWheelScrollDelta || !UiMotion.Enabled || !Visible)
        {
            if (motion.Enabled) StopMotion();
            target = Math.Clamp(position + distance, 0, Maximum);
            ApplyPosition(target);
            bar.NotifyValueChanged();
            return;
        }
        double basis = motion.Enabled && Math.Sign(distance) == Math.Sign(target - position) ? target : position;
        target = Math.Clamp(basis + distance, 0, Maximum);
        if (Math.Abs(target - position) < .5) { ScrollTo((int)Math.Round(target)); return; }
        // An immediate partial step keeps the first response independent of timer latency.
        ApplyPosition(position + (target - position) * .18);
        clock.Restart();
        lastSample = 0;
        motion.Start();
    }

    private void Advance()
    {
        if (IsDisposed || !Visible || !UiMotion.Enabled) { FinishWheel(); return; }
        double elapsed = clock.Elapsed.TotalSeconds;
        double seconds = Math.Max(0, elapsed - lastSample);
        lastSample = elapsed;
        // Time-based exponential settling: 95% of remaining travel in about 135 ms.
        double next = target + (position - target) * Math.Exp(-seconds / .045);
        if (Math.Abs(next - target) < .5 || elapsed >= .35) { FinishWheel(); return; }
        ApplyPosition(next);
    }

    private void FinishWheel()
    {
        ScrollTo((int)Math.Round(target));
        bar.NotifyValueChanged();
    }

    private void ApplyPosition(double value)
    {
        position = Math.Clamp(value, 0, Maximum);
        int rounded = (int)Math.Round(position);
        if (Offset == rounded) { lastWritten = rounded; return; }
        long started = Stopwatch.GetTimestamp();
        applying = true;
        try
        {
            viewport.AutoScrollPosition = new Point(0, rounded);
            lastWritten = Offset;
            // Native scroll bit-copying can retain pixels from child HWNDs at their old
            // positions. Repaint the viewport subtree; header and footer stay untouched.
            viewport.Invalidate(true);
            bar.Invalidate();
        }
        finally { applying = false; }
        long ticks = Stopwatch.GetTimestamp() - started;
        ScrollUpdateTicks += ticks;
        MaximumScrollUpdateTicks = Math.Max(MaximumScrollUpdateTicks, ticks);
        ScrollUpdates++;
    }

    private void StopMotion()
    {
        motion.Stop();
        clock.Stop();
        lastWritten = Offset;
        position = target = lastWritten;
    }

    private void ScrollTo(int value)
    {
        StopMotion();
        ApplyPosition(value);
        target = position;
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
        StopMotion();
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
            if (lastMaximum != Maximum)
            {
                StopMotion();
                lastMaximum = Maximum;
            }
            bar.Invalidate();
        }
        finally { arranging = false; }
    }

    private sealed class SettingsScrollBar : Control
    {
        private readonly SettingsScrollHost host;
        private Panel Viewport => host.viewport;
        private bool hovering;
        private bool dragging;
        private int dragOffset;
        internal CapturePalette Palette { get; set; } = CapturePalette.Light;
        private int Offset => host.Offset;
        private int Maximum => host.Maximum;
        private int Scale(int value) => (int)Math.Ceiling(value * DeviceDpi / 96f);

        internal SettingsScrollBar(SettingsScrollHost host)
        {
            this.host = host;
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
                    (int)((long)track.Height * Viewport.ClientSize.Height / Math.Max(1, Viewport.DisplayRectangle.Height))));
                int y = track.Top + (int)((long)(track.Height - height) * Offset / Math.Max(1, Maximum));
                return new Rectangle(0, y, Width, height);
            }
        }

        private void ScrollTo(int value)
        {
            host.ScrollTo(value);
            Invalidate();
            NotifyValueChanged();
        }

        internal void NotifyValueChanged() => AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);

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
            host.StopMotion();
            Focus();
            Rectangle thumb = Thumb;
            if (thumb.Contains(e.Location))
            {
                dragOffset = e.Y - thumb.Top;
                dragging = true;
                Capture = true;
                Invalidate();
            }
            else ScrollTo(Offset + (e.Y < thumb.Top ? -1 : 1) * Math.Max(1, Viewport.ClientSize.Height - Scale(32)));
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
            host.ScrollWheel(e.Delta);
        }

        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is
            Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int page = Math.Max(1, Viewport.ClientSize.Height - Scale(32));
            int? target = e.KeyCode switch
            {
                Keys.Up => Offset - Viewport.VerticalScroll.SmallChange,
                Keys.Down => Offset + Viewport.VerticalScroll.SmallChange,
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) { motion.Stop(); motion.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class SettingsScrollViewport : Panel
{
    internal Action<int>? WheelRequested { get; set; }
    internal SettingsScrollViewport() => DoubleBuffered = true;
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= 0x02000000; // WS_EX_COMPOSITED: buffer the viewport's native child windows together.
            return parameters;
        }
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (WheelRequested == null) { base.OnMouseWheel(e); return; }
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
        WheelRequested(e.Delta);
    }
}
