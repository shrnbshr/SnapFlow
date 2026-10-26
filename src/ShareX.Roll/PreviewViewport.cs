// GPL-3.0-or-later. View transforms affect presentation only; Image remains the original capture.
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using static ShareX.Roll.Localization.UiText;

namespace ShareX.Roll;

internal sealed partial class PreviewCanvas
{
    private enum PreviewMode { Fit, Zoom, Width }
    private PreviewMode viewMode;
    private Image? viewImage;
    private readonly ScrollFrameClock viewportTimer;
    private readonly Stopwatch viewportClock = new();
    private double viewportSample;
    private SpringAxis zoom = new(1), offsetX = new(0), offsetY = new(0);
    private bool panning, draggingThumb;
    private Point lastPointer;
    private float thumbGrab;
    private PreviewZoomBar zoomBar = null!;
    private PreviewZoomFade zoomBarFade = null!;
    private bool hoverCheckPending;
    private PreviewWheelFilter? wheelFilter;
    private bool zoomTransparencyEnabled = true;
    internal bool ZoomTransparencyEnabled
    {
        get => zoomTransparencyEnabled;
        set
        {
            if (zoomTransparencyEnabled == value) return;
            zoomTransparencyEnabled = value;
            zoomBar?.InvalidateBackdrop();
            zoomBarFade?.RefreshSnapshot();
        }
    }
    internal double ZoomFactor => zoom.Value;
    internal double ZoomTarget => zoom.Target;
    internal bool IsWidthReading => viewMode == PreviewMode.Width;
    internal bool IsViewportAnimating => viewportTimer.Enabled;
    internal RectangleF ImageBounds => Image == null ? RectangleF.Empty : new(
        (float)ClampOffset(offsetX.Value, Image.Width * zoom.Value, ViewWidth, 0),
        (float)ClampOffset(offsetY.Value, Image.Height * zoom.Value, Height, BottomSpace(zoom.Value)),
        (float)(Image.Width * zoom.Value), (float)(Image.Height * zoom.Value));
    private float D(float value) => value * DeviceDpi / 96f;
    // The rail has its own hit area; reading has no horizontal overflow.
    private double ViewWidth => viewMode == PreviewMode.Fit ? Width : Math.Max(1, Width - D(20));
    private double FitScale => Image == null ? 1 : Math.Min((double)Width / Image.Width, (double)Height / Image.Height);
    private double MinimumScale => Math.Min(.05, FitScale);
    private double MaximumScale => Math.Max(8, FitScale);
    private double BottomSpace(double scale) => Image != null && Image.Height * scale > Height ? zoomBar.FaceBounds.Height + D(24) : 0;

    private void InitializeViewport()
    {
        zoomBar = new PreviewZoomBar(this) { Name = "previewZoomBar", Visible = false };
        Controls.Add(zoomBar);
        zoomBarFade = new PreviewZoomFade(this, zoomBar) { Name = "previewZoomFade", Visible = false };
        Controls.Add(zoomBarFade);
        foreach (Control control in new Control[] { this, zoomBar }.Concat(zoomBar.Controls.Cast<Control>()))
        {
            control.MouseEnter += (_, _) => SetZoomBarHovered(true);
            control.MouseLeave += (_, _) => QueueZoomBarHoverCheck();
        }
        wheelFilter = new PreviewWheelFilter(this);
        Application.AddMessageFilter(wheelFilter);
        HandleDestroyed += (_, _) => { hoverCheckPending = false; StopViewportMotion(); zoomBarFade.HideImmediately(); };
        MouseCaptureChanged += (_, _) => { if (!Capture) { panning = draggingThumb = false; UpdatePanCursor(); } };
        ApplyViewportLanguage();
    }

    internal void SynchronizeImage()
    {
        if (ReferenceEquals(viewImage, Image)) return;
        EndReveal();
        viewImage = Image;
        viewMode = PreviewMode.Fit;
        ResetTransform();
        UpdateZoomBarHover();
        ApplyViewportLanguage();
        UpdatePanCursor();
    }

    private void ResetTransform()
    {
        viewportTimer.Stop();
        viewportClock.Stop();
        zoom.Snap(Math.Max(.000001, FitScale));
        offsetX.Snap(Image == null ? 0 : (Width - Image.Width * zoom.Value) / 2);
        offsetY.Snap(Image == null ? 0 : (Height - Image.Height * zoom.Value) / 2);
        UpdateViewportControls();
    }

    private void ResizeViewport()
    {
        if (zoomBar == null || Width < 1 || Height < 1) return;
        zoomBar.Arrange();
        zoomBarFade?.RefreshSnapshot();
        SynchronizeImage();
        if (Image == null) return;
        if (viewMode == PreviewMode.Fit) ResetTransform();
        else if (viewMode == PreviewMode.Width)
        {
            double sourceY = -offsetY.Value / Math.Max(.000001, zoom.Value);
            zoom.Snap(ViewWidth / Image.Width);
            offsetX.Snap(0);
            offsetY.Snap(-sourceY * zoom.Value);
            ClampTargets();
            StopViewportMotion();
        }
        else { ClampTargets(); StopViewportMotion(); }
        UpdateViewportControls();
    }

    internal void ApplyViewportAppearance()
    {
        zoomBar?.ApplyPalette(Palette);
        zoomBarFade?.RefreshSnapshot();
        Invalidate();
    }

    internal void ApplyViewportLanguage()
    {
        AccessibleName = L("ImagePreview");
        AccessibleDescription = L(IsWidthReading ? "PreviewReadingHelp" : "PreviewZoomHelp");
        zoomBar?.ApplyLanguage();
        zoomBarFade?.RefreshSnapshot();
    }

    internal void SetZoomBarHovered(bool hovered) => zoomBarFade.SetShown(hovered && Image != null && Visible);

    internal void UpdateZoomBarPointer(Point screenPosition) =>
        SetZoomBarHovered(IsHandleCreated && ClientRectangle.Contains(PointToClient(screenPosition)));

    private void UpdateZoomBarHover() => UpdateZoomBarPointer(Cursor.Position);

    private void QueueZoomBarHoverCheck()
    {
        if (hoverCheckPending || !IsHandleCreated || IsDisposed || Disposing) return;
        hoverCheckPending = true;
        BeginInvoke((Action)(() =>
        {
            hoverCheckPending = false;
            if (!IsDisposed && !Disposing) UpdateZoomBarHover();
        }));
    }

    internal void PaintZoomBackdrop(Graphics graphics, Rectangle bounds)
    {
        graphics.Clear(BackColor);
        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(-bounds.X, -bounds.Y);
        PaintImage(graphics);
        PaintViewportChrome(graphics);
        graphics.Restore(state);
    }

    protected override void OnInvalidated(InvalidateEventArgs e)
    {
        base.OnInvalidated(e);
        // Child windows do not automatically repaint when the image beneath them moves.
        zoomBar?.InvalidateBackdrop();
        zoomBarFade?.RefreshSnapshot();
    }

    private void PaintZoomControlsInCurrentFrame()
    {
        if (zoomBar?.Visible != true) return;
        // UpdateWindow paints each child now. Queued child WM_PAINT messages can otherwise
        // lag behind the canvas during continuous dragging, scrolling or spring animation.
        zoomBar.Update();
        foreach (Control button in zoomBar.Controls) button.Update();
        if (zoomBarFade?.Visible == true) zoomBarFade.Update();
    }

    internal void ZoomBy(double steps, PointF anchor)
    {
        SynchronizeImage();
        if (Image == null || steps == 0) return;
        EndReveal();
        RectangleF displayedBounds = ImageBounds;
        // An edge can constrain the painted position during a spring. Rebase only those constrained
        // axes before retargeting; unconstrained axes retain their velocity.
        if (Math.Abs(offsetX.Value - displayedBounds.X) > .001) offsetX.Snap(displayedBounds.X);
        if (Math.Abs(offsetY.Value - displayedBounds.Y) > .001) offsetY.Snap(displayedBounds.Y);
        double sourceX = (anchor.X - displayedBounds.X) / zoom.Value;
        double sourceY = (anchor.Y - displayedBounds.Y) / zoom.Value;
        viewMode = PreviewMode.Zoom;
        zoom.Target = Math.Clamp(zoom.Target * Math.Pow(1.2, Math.Clamp(steps, -12, 12)), MinimumScale, MaximumScale);
        offsetX.Target = anchor.X - sourceX * zoom.Target;
        offsetY.Target = anchor.Y - sourceY * zoom.Target;
        StartViewportMotion();
        ApplyViewportLanguage();
    }

    internal void FitImage()
    {
        SynchronizeImage();
        if (Image == null) return;
        EndReveal();
        viewMode = PreviewMode.Fit;
        zoom.Target = FitScale;
        offsetX.Target = (Width - Image.Width * zoom.Target) / 2;
        offsetY.Target = (Height - Image.Height * zoom.Target) / 2;
        StartViewportMotion();
        ApplyViewportLanguage();
    }

    internal void FitImageWidth()
    {
        SynchronizeImage();
        if (Image == null) return;
        EndReveal();
        viewMode = PreviewMode.Width;
        zoom.Target = ViewWidth / Image.Width;
        offsetX.Target = 0;
        offsetY.Target = 0; // Enter reading at the top, as with opening a document.
        StartViewportMotion();
        ApplyViewportLanguage();
    }

    internal void ScrollPreview(double pixels)
    {
        if (Image == null) return;
        EndReveal();
        offsetY.Target -= pixels;
        StartViewportMotion();
    }

    private void ClampTargets()
    {
        if (Image == null) return;
        offsetX.Target = ClampOffset(offsetX.Target, Image.Width * zoom.Target, ViewWidth, 0);
        offsetY.Target = ClampOffset(offsetY.Target, Image.Height * zoom.Target, Height, BottomSpace(zoom.Target));
    }

    private static double ClampOffset(double offset, double content, double viewport, double trailing) =>
        content <= viewport ? (viewport - content) / 2 : Math.Clamp(offset, viewport - content - trailing, 0);

    private void StartViewportMotion()
    {
        ClampTargets();
        UpdateViewportControls();
        if (!UiMotion.Enabled || !Visible || !IsHandleCreated) { StopViewportMotion(); return; }
        // Re-target from current position AND velocity, so rapid direction changes never restart a tween.
        if (!viewportTimer.Enabled) { viewportSample = 0; viewportClock.Restart(); viewportTimer.Start(); }
        Invalidate();
    }

    private void AdvanceViewport()
    {
        if (!Visible || !UiMotion.Enabled || Image == null) { StopViewportMotion(); return; }
        double now = viewportClock.Elapsed.TotalSeconds;
        double elapsed = now - viewportSample;
        viewportSample = now;
        zoom.Advance(elapsed); offsetX.Advance(elapsed); offsetY.Advance(elapsed);
        if (zoom.Settled(.00001) && offsetX.Settled(.05) && offsetY.Settled(.05)) StopViewportMotion();
        UpdateViewportControls();
        Invalidate();
    }

    private void StopViewportMotion()
    {
        viewportTimer.Stop(); viewportClock.Stop();
        zoom.Snap(zoom.Target); offsetX.Snap(offsetX.Target); offsetY.Snap(offsetY.Target);
        if (zoomBar != null && !IsDisposed) UpdateViewportControls();
        Invalidate();
    }

    private void UpdateViewportControls()
    {
        if (zoomBar == null) return;
        zoomBar.UpdateValues(zoom.Value, zoom.Target > MinimumScale + .000001,
            zoom.Target < MaximumScale - .000001, IsWidthReading);
        if (zoomBarFade?.Visible == true) zoomBarFade.Invalidate();
        UpdatePanCursor();
    }

    private void UpdatePanCursor() => Cursor = panning || draggingThumb ? Cursors.SizeAll :
        Image != null && (ImageBounds.Width > Width + 1 || ImageBounds.Height > Height + 1) ? Cursors.Hand : Cursors.Default;

    private void PaintImage(Graphics graphics)
    {
        if (Image == null) return;
        if (viewMode == PreviewMode.Fit && !viewportTimer.Enabled && zoom.Value == FitScale)
        {
            // Preserve PictureBox's exact settled fit. A drag can stop the fit spring halfway;
            // that interrupted presentation must keep its current scale instead of jumping to fit.
            base.OnPaint(new PaintEventArgs(graphics, ClientRectangle));
            return;
        }
        double scale = zoom.Value;
        double x = ClampOffset(offsetX.Value, Image.Width * scale, ViewWidth, 0);
        double y = ClampOffset(offsetY.Value, Image.Height * scale, Height, BottomSpace(scale));
        RectangleF source = RectangleF.Intersect(new RectangleF((float)(-x / scale), (float)(-y / scale),
            (float)(ViewWidth / scale), (float)(Height / scale)), new RectangleF(0, 0, Image.Width, Image.Height));
        if (source.Width <= 0 || source.Height <= 0) return;
        RectangleF destination = new((float)(x + source.X * scale), (float)(y + source.Y * scale),
            (float)(source.Width * scale), (float)(source.Height * scale));
        GraphicsState saved = graphics.Save();
        graphics.SetClip(new RectangleF(0, 0, (float)ViewWidth, Height), CombineMode.Intersect);
        // Only sample visible source pixels. Never allocate a bitmap at the scaled long-image size.
        graphics.InterpolationMode = viewportTimer.Enabled || panning ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using System.Drawing.Imaging.ImageAttributes attributes = new();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        graphics.DrawImage(Image, Rectangle.Round(destination), source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        graphics.Restore(saved);
    }

    private RectangleF ScrollTrack => new(Width - D(20), D(6), D(20), Math.Max(1, Height - D(12)));
    private RectangleF ScrollThumb
    {
        get
        {
            if (Image == null || Image.Height * zoom.Value <= Height) return RectangleF.Empty;
            RectangleF track = ScrollTrack;
            double content = Image.Height * zoom.Value + BottomSpace(zoom.Value);
            float height = Math.Min(track.Height, Math.Max(D(32), (float)(track.Height * Height / content)));
            double range = content - Height;
            float top = track.Top + (float)(Math.Clamp(-offsetY.Value / range, 0, 1) * (track.Height - height));
            return new(track.Left, top, track.Width, height);
        }
    }

    private void PaintViewportChrome(Graphics g)
    {
        RectangleF thumb = ScrollThumb;
        if (!thumb.IsEmpty)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using SolidBrush track = new(Palette.Canvas);
            g.FillRectangle(track, ScrollTrack);
            using SolidBrush ink = new(Palette.Secondary);
            using GraphicsPath path = CaptureDrawing.Rounded(new RectangleF(thumb.X + D(7), thumb.Y, D(6), thumb.Height), D(3));
            g.FillPath(ink, path);
        }
        if (Focused && ShowFocusCues)
        {
            using Pen pen = new(Palette.Accent, D(1.5f));
            g.DrawRectangle(pen, 2, 2, Math.Max(1, Width - 5), Math.Max(1, Height - 5));
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Image != null)
        {
            if (e is HandledMouseEventArgs handled) handled.Handled = true;
            if (IsWidthReading && (ModifierKeys & Keys.Control) == 0)
            {
                int lines = SystemInformation.MouseWheelScrollLines;
                if (lines != 0) ScrollPreview(-e.Delta / 120d * (lines < 0 ? Height * .9 : D(22) * lines));
            }
            else ZoomBy(e.Delta / 120d, e.Location);
        }
        base.OnMouseWheel(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Image == null || e.Button != MouseButtons.Left) return;
        Focus(); EndReveal();
        // A grab interrupts at the currently displayed transform, never at its future target.
        viewportTimer.Stop(); viewportClock.Stop();
        RectangleF bounds = ImageBounds;
        zoom.Snap(zoom.Value); offsetX.Snap(bounds.X); offsetY.Snap(bounds.Y);
        RectangleF thumb = ScrollThumb;
        if (!thumb.IsEmpty && ScrollTrack.Contains(e.Location))
        {
            if (thumb.Contains(e.Location)) { draggingThumb = true; thumbGrab = e.Y - thumb.Top; Capture = true; }
            else ScrollPreview(e.Y < thumb.Top ? -Height * .9 : Height * .9);
        }
        else { panning = true; lastPointer = e.Location; Capture = true; }
        UpdatePanCursor();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Image == null) return;
        if (panning)
        {
            offsetX.Target = offsetX.Value + e.X - lastPointer.X;
            offsetY.Target = offsetY.Value + e.Y - lastPointer.Y;
            ClampTargets();
            offsetX.Snap(offsetX.Target); offsetY.Snap(offsetY.Target);
            lastPointer = e.Location;
            Invalidate();
        }
        else if (draggingThumb)
        {
            RectangleF track = ScrollTrack, thumb = ScrollThumb;
            double fraction = Math.Clamp((e.Y - thumbGrab - track.Top) / Math.Max(1, track.Height - thumb.Height), 0, 1);
            offsetY.Snap(-fraction * Math.Max(0, Image.Height * zoom.Value + BottomSpace(zoom.Value) - Height));
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        panning = draggingThumb = false; Capture = false;
        UpdatePanCursor(); Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right
        or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Image != null)
        {
            Point center = new(Width / 2, Height / 2);
            switch (e.KeyCode)
            {
                case Keys.Add: case Keys.Oemplus: ZoomBy(1, center); break;
                case Keys.Subtract: case Keys.OemMinus: ZoomBy(-1, center); break;
                case Keys.D0: case Keys.NumPad0: FitImage(); break;
                case Keys.W: FitImageWidth(); break;
                case Keys.Up: ScrollPreview(-D(44)); break;
                case Keys.Down: ScrollPreview(D(44)); break;
                case Keys.PageUp: ScrollPreview(-Height * .9); break;
                case Keys.PageDown: case Keys.Space: ScrollPreview(Height * .9); break;
                case Keys.Home: offsetY.Target = 0; StartViewportMotion(); break;
                case Keys.End: ScrollPreview(double.MaxValue); break;
                case Keys.Left: offsetX.Target += D(44); StartViewportMotion(); break;
                case Keys.Right: offsetX.Target -= D(44); StartViewportMotion(); break;
                default: base.OnKeyDown(e); return;
            }
            e.Handled = e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); ResizeViewport(); }

    private void DisposeViewport()
    {
        if (wheelFilter != null) { Application.RemoveMessageFilter(wheelFilter); wheelFilter = null; }
        viewportTimer.Stop(); viewportTimer.Dispose();
    }

    // Route by hover rather than keyboard focus, without intercepting other windows or capture overlays.
    private sealed class PreviewWheelFilter(PreviewCanvas canvas) : IMessageFilter
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A || canvas.IsDisposed || !canvas.Visible || canvas.Image == null) return false;
            long position = m.LParam.ToInt64();
            Point screen = new((short)(position & 0xffff), (short)((position >> 16) & 0xffff));
            Point local = canvas.PointToClient(screen);
            if (!canvas.ClientRectangle.Contains(local)) return false;
            Control? hit = Control.FromChildHandle(WindowFromPoint(screen));
            if (hit != canvas && (hit == null || !canvas.Contains(hit))) return false;
            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xffff);
            canvas.OnMouseWheel(new HandledMouseEventArgs(MouseButtons.None, 0, local.X, local.Y, delta));
            return true;
        }
    }

    private struct SpringAxis(double initial)
    {
        internal double Value = initial, Target = initial, Velocity;
        internal void Snap(double value) { Value = Target = value; Velocity = 0; }
        internal bool Settled(double tolerance) => Math.Abs(Value - Target) < tolerance && Math.Abs(Velocity) < tolerance * 15;
        internal void Advance(double seconds)
        {
            const double frequency = 32; // Critically damped, about 220 ms to 99%; no overshoot decoration.
            double displacement = Value - Target, coefficient = Velocity + frequency * displacement;
            double decay = Math.Exp(-frequency * seconds);
            Value = Target + (displacement + coefficient * seconds) * decay;
            Velocity = (Velocity - frequency * coefficient * seconds) * decay;
        }
    }
}

internal sealed class PreviewZoomBar : Panel
{
    private readonly PreviewCanvas viewport;
    private readonly Button minus = new() { Name = "previewZoomOut", Text = "−" };
    private readonly Button percent = new() { Name = "previewZoomPercent" };
    private readonly Button plus = new() { Name = "previewZoomIn", Text = "+" };
    private readonly Button width = new() { Name = "previewFitWidth" };
    private readonly ToolTip tips = new();
    private readonly PreviewZoomBackdrop backdrop = new();
    private Bitmap? shadow;
    private float shadowScale;
    private CapturePalette palette = CapturePalette.Light;
    private bool reading;
    private string scaleText = "100%";
    private int ShadowPadding => (int)Math.Round(12 * viewport.DeviceDpi / 96f);
    internal RectangleF FaceBounds => new(ShadowPadding, ShadowPadding,
        Width - 2 * ShadowPadding, Height - 2 * ShadowPadding);

    internal PreviewZoomBar(PreviewCanvas viewport)
    {
        this.viewport = viewport;
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.ToolBar;
        foreach (Button button in new[] { minus, percent, plus, width })
        {
            button.Tag = "quiet";
            button.Font = new Font("Microsoft YaHei UI", button == minus || button == plus ? 13 : 10);
            button.AutoSize = false;
            button.Padding = button.Margin = Padding.Empty;
            button.TabIndex = Controls.Count;
            Controls.Add(button);
            // Wheel events over the floating controls still affect the preview.
            button.MouseWheel += (_, e) => viewport.ZoomBy(e.Delta / 120d, new PointF(viewport.Width / 2f, viewport.Height / 2f));
        }
        minus.Click += (_, _) => viewport.ZoomBy(-1, Center());
        plus.Click += (_, _) => viewport.ZoomBy(1, Center());
        percent.Click += (_, _) => viewport.FitImage();
        width.Click += (_, _) => { if (reading) viewport.FitImage(); else viewport.FitImageWidth(); };
        ApplyLanguage();
        ApplyPalette(palette);
    }

    private PointF Center() => new(viewport.Width / 2f, viewport.Height / 2f);

    internal void Arrange()
    {
        float scale = viewport.DeviceDpi / 96f;
        int D(int value) => (int)Math.Round(value * scale);
        // The 32px targets stay usable in the minimum-width preview at 96–200% DPI.
        int fitWidth = Math.Max(D(84), TextRenderer.MeasureText(width.Text, width.Font).Width + D(12));
        int[] widths = [D(32), D(66), D(32), fitWidth];
        int padding = ShadowPadding;
        int faceWidth = widths.Sum() + D(16), faceHeight = D(44);
        // Reserve the shadow halo inside this one control, so its native fade also fades the shadow.
        // The visible face and all button hit targets keep their original screen positions.
        Size = new Size(faceWidth + 2 * padding, faceHeight + 2 * padding);
        Location = new Point(Math.Max(0, viewport.Width - faceWidth - D(28) - padding),
            Math.Max(0, viewport.Height - faceHeight - D(12) - padding));
        int x = padding + D(6);
        Button[] buttons = [minus, percent, plus, width];
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].Bounds = new Rectangle(x, padding + D(6), widths[i], D(32));
            x += widths[i] + (i == 2 ? D(4) : 0);
        }
    }

    internal void ApplyPalette(CapturePalette value)
    {
        palette = value;
        BackColor = palette.Surface;
        foreach (Button button in Controls) palette.StyleButton(button);
        CapturePalette fitPalette = reading ? palette with { Accent = palette.Tint, AccentText = palette.Text } : palette;
        fitPalette.StyleButton(width, reading);
        InvalidateBackdrop();
    }

    internal void ApplyLanguage()
    {
        AccessibleName = L("PreviewZoomControls");
        minus.AccessibleName = L("PreviewZoomOut"); plus.AccessibleName = L("PreviewZoomIn");
        percent.AccessibleName = L("PreviewZoomScale") + " " + scaleText;
        percent.AccessibleDescription = L("PreviewFitImage");
        width.Text = L(reading ? "PreviewFitImage" : "PreviewFitWidth");
        width.AccessibleName = width.Text;
        tips.SetToolTip(minus, L("PreviewZoomOut") + " (−)");
        tips.SetToolTip(plus, L("PreviewZoomIn") + " (+)");
        tips.SetToolTip(percent, L("PreviewFitImage") + " (0)");
        tips.SetToolTip(width, L(reading ? "PreviewZoomHelp" : "PreviewReadingHelp"));
        tips.SetToolTip(this, L(reading ? "PreviewReadingHelp" : "PreviewZoomHelp"));
        UiLanguage.ApplyTextDirection(this);
        Arrange();
    }

    internal void UpdateValues(double factor, bool canShrink, bool canGrow, bool isReading)
    {
        string text = (factor * 100).ToString(factor * 100 < 10 ? "0.#" : "0") + "%";
        if (scaleText != text || percent.Text.Length == 0)
        {
            scaleText = text; percent.Text = text;
            percent.AccessibleName = L("PreviewZoomScale") + " " + text;
        }
        minus.Enabled = canShrink; plus.Enabled = canGrow;
        if (reading != isReading) { reading = isReading; ApplyLanguage(); ApplyPalette(palette); }
    }

    internal void PaintSurface(Graphics graphics, Point origin)
    {
        // All button windows sample the same blurred image, keeping the material continuous.
        bool transparent = viewport.ZoomTransparencyEnabled && !SystemInformation.HighContrast;
        if (origin == Point.Empty) viewport.PaintZoomBackdrop(graphics, Bounds);
        else graphics.Clear(palette.Surface);
        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(-origin.X, -origin.Y);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = CaptureDrawing.Rounded(FaceBounds, 14 * DeviceDpi / 96f);
        if (origin == Point.Empty && !SystemInformation.HighContrast)
            PaintShadow(graphics);
        if (transparent)
            backdrop.Paint(graphics, viewport, Bounds, path,
                Color.FromArgb(palette.Surface.GetBrightness() < .5f ? 222 : 218, palette.Surface));
        else
        {
            using SolidBrush fill = new(palette.Surface);
            graphics.FillPath(fill, path);
        }
        graphics.Restore(state);
    }

    private void PaintShadow(Graphics graphics)
    {
        float scale = DeviceDpi / 96f;
        if (shadow == null || shadow.Size != Size || shadowScale != scale)
        {
            shadow?.Dispose();
            shadow = ButtonElevation.CreateShadow(Size, scale, 2.6f, 1.4f, FaceBounds, 14 * scale, 2.6f, false);
            shadowScale = scale;
        }
        using ImageAttributes opacity = new();
        opacity.SetColorMatrix(new ColorMatrix { Matrix33 = palette.Surface.GetBrightness() < .5f ? .4f : .16f });
        graphics.DrawImage(shadow, ClientRectangle, 0, 0, Width, Height, GraphicsUnit.Pixel, opacity);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => PaintSurface(e.Graphics, Point.Empty);

    internal void InvalidateBackdrop()
    {
        backdrop.Invalidate();
        Invalidate(true);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x84) // Preserve click-through corners without a jagged native window region.
        {
            long position = message.LParam.ToInt64();
            Point local = PointToClient(new Point((short)position, (short)(position >> 16)));
            using GraphicsPath path = CaptureDrawing.Rounded(FaceBounds, 14 * DeviceDpi / 96f);
            if (!path.IsVisible(local)) { message.Result = new IntPtr(-1); return; }
        }
        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing) { if (disposing) { tips.Dispose(); backdrop.Dispose(); shadow?.Dispose(); } base.Dispose(disposing); }
}
