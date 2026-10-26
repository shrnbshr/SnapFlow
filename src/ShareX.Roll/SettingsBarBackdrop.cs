// GPL-3.0-or-later. Frost only the two fixed settings bars using the scrolling content beneath them.
using ShareX.HelpersLib;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace ShareX.Roll;

internal interface IBackdropSnapshotOverlay
{
    void ApplySnapshotOverlay(BackdropSnapshot snapshot);
}

internal sealed class SettingsBarBackdrop : IDisposable
{
    private readonly Control bar, content;
    private readonly Dictionary<Control, Surface> surfaces = new();
    private readonly HashSet<Control> sources = new();
    private readonly Dictionary<Control, int> sourceVersions = new();
    private List<SampleState> sampled = [], pending = [];
    private bool sampleValid;
    private float sampleDpi;
    internal long CaptureTicks, FilterTicks, UpscaleTicks, AlphaTicks;
    internal int Builds, Reuses;
    private readonly record struct SampleState(Control Control, Rectangle Clip, Point Origin, Size Size, int Version, int Background);
    private CapturePalette palette = CapturePalette.Light;
    private Bitmap? capture, sample, material;
    private int[] materialPixels = [], livePixels = [];
    private bool enabled, dirty = true, sampling, invalidating, disposed;

    internal SettingsBarBackdrop(Control bar, Control content)
    {
        this.bar = bar;
        this.content = content;
        ObserveBar(bar);
        ObserveSource(content.Parent ?? content);
    }

    internal void Apply(CapturePalette value, bool active)
    {
        palette = value;
        enabled = active && !SystemInformation.HighContrast;
        sampleValid = false;
        dirty = false;
        Changed(null, EventArgs.Empty);
    }

    internal void ApplySnapshot(BackdropSnapshot snapshot, Control page)
    {
        if (!enabled || disposed) return;
        Rectangle barBounds = page.RectangleToClient(bar.RectangleToScreen(bar.ClientRectangle));
        foreach ((Control control, Surface surface) in surfaces)
        {
            if (!control.Visible) continue;
            Point origin = page.PointToClient(control.PointToScreen(Point.Empty));
            Rectangle area = Rectangle.Intersect(new Rectangle(origin, control.ClientSize), barBounds);
            area.Intersect(new Rectangle(Point.Empty, snapshot.Size));
            surface.ApplySnapshot(snapshot, origin, area, palette.Background.ToArgb() & 0xFFFFFF);
        }
    }

    private void ObserveBar(Control control)
    {
        if (surfaces.ContainsKey(control)) return;
        surfaces.Add(control, new Surface(this, control));
        control.ControlAdded += BarAdded;
        control.LocationChanged += Changed;
        control.SizeChanged += Changed;
        foreach (Control child in control.Controls) ObserveBar(child);
    }

    private void BarAdded(object? sender, ControlEventArgs e) { if (e.Control != null) ObserveBar(e.Control); }

    private void ObserveSource(Control control)
    {
        if (!sources.Add(control)) return;
        control.Invalidated += SourceInvalidated;
        control.TextChanged += SourceChanged;
        control.FontChanged += SourceChanged;
        control.BackColorChanged += SourceChanged;
        control.ForeColorChanged += SourceChanged;
        control.EnabledChanged += SourceChanged;
        control.LocationChanged += Changed;
        control.SizeChanged += Changed;
        control.ControlAdded += SourceAdded;
        foreach (Control child in control.Controls) ObserveSource(child);
    }

    private void SourceAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control != null) ObserveSource(e.Control);
        Changed(sender, EventArgs.Empty);
    }
    private void SourceInvalidated(object? sender, InvalidateEventArgs e) => SourceChanged(sender, EventArgs.Empty);
    private void SourceChanged(object? sender, EventArgs e)
    {
        if (sampling) return;
        if (sender is Control control) sourceVersions[control] = sourceVersions.GetValueOrDefault(control) + 1;
        Changed(sender, e);
    }

    private static bool BackgroundOnly(Control control) => control is IBackdropBackground ||
        control is Panel { BorderStyle: BorderStyle.None, BackgroundImage: null } &&
        (control.GetType() == typeof(Panel) || control.GetType() == typeof(FlowLayoutPanel) ||
            control.GetType() == typeof(TableLayoutPanel) && ((TableLayoutPanel)control).CellBorderStyle == TableLayoutPanelCellBorderStyle.None ||
            control is SettingsReveal);

    private void CollectSample(Control control, Point origin, Rectangle parentClip, Rectangle area)
    {
        if (!control.Visible) return;
        Rectangle clip = Rectangle.Intersect(Rectangle.Intersect(new Rectangle(origin, control.ClientSize), parentClip), area);
        if (clip.IsEmpty) return;
        Rectangle relative = clip; relative.Offset(-area.X, -area.Y);
        bool background = BackgroundOnly(control);
        pending.Add(new(control, relative, background ? Point.Empty : new Point(origin.X - area.X, origin.Y - area.Y),
            background ? Size.Empty : control.ClientSize, background ? 0 : sourceVersions.GetValueOrDefault(control), control.BackColor.ToArgb()));
        foreach (Control child in control.Controls.Cast<Control>().Reverse())
            CollectSample(child, new Point(origin.X + child.Left, origin.Y + child.Top), clip, area);
    }

    private void Changed(object? sender, EventArgs e)
    {
        if (disposed || sampling || dirty) return;
        dirty = true;
        if (bar.IsHandleCreated && bar.Visible)
        {
            invalidating = true;
            try { bar.Invalidate(true); }
            finally { invalidating = false; }
        }
    }

    private void EnsureMaterial()
    {
        if (!dirty || sampling || bar.Width <= 0 || bar.Height <= 0) return;
        sampling = true;
        try
        {
            float dpi = bar.DeviceDpi / 96f;
            int padding = (int)Math.Ceiling(24 * dpi);
            Point origin = content.PointToClient(bar.PointToScreen(Point.Empty));
            Rectangle area = new(origin.X - padding, origin.Y - padding, bar.Width + padding * 2, bar.Height + padding * 2);
            // Layout and offscreen invalidations need not regenerate an unchanged sample.
            // Compare exact visible source state; retain live revisions for animated/text controls.
            pending.Clear();
            CollectSample(content, Point.Empty, new Rectangle(Point.Empty, content.ClientSize), area);
            if (sampleValid && sampleDpi == dpi && material?.Size == bar.ClientSize && pending.SequenceEqual(sampled))
            {
                Reuses++;
                dirty = false;
                return;
            }
            Resize(ref capture, area.Size, PixelFormat.Format32bppRgb);
            long stage = Stopwatch.GetTimestamp();
            using (Graphics graphics = Graphics.FromImage(capture!))
            {
                graphics.Clear(palette.Background);
                nint dc = graphics.GetHdc();
                try { PrintContent(content, Point.Empty, new Rectangle(Point.Empty, content.ClientSize), area, dc); GdiFlush(); }
                finally { graphics.ReleaseHdc(dc); }
            }
            // RGB ignores GDI's unspecified alpha just as the previous opaque normalization
            // did, without copying the full capture out of and back into managed memory.
            CaptureTicks += Stopwatch.GetTimestamp() - stage; stage = Stopwatch.GetTimestamp();
            Resize(ref sample, new Size(Math.Max(1, (int)Math.Ceiling(area.Width / (4 * dpi))),
                Math.Max(1, (int)Math.Ceiling(area.Height / (4 * dpi)))));
            using (Graphics graphics = Graphics.FromImage(sample!))
                graphics.DrawImage(capture!, new Rectangle(Point.Empty, sample!.Size));
            // Reuse the same small-sample blur approach as PreviewZoomBackdrop.
            ImageHelpers.FastBoxBlur(sample!, 4);
            ImageHelpers.FastBoxBlur(sample!, 4);
            FilterTicks += Stopwatch.GetTimestamp() - stage; stage = Stopwatch.GetTimestamp();
            Resize(ref material, bar.ClientSize);
            using (Graphics graphics = Graphics.FromImage(material!))
            {
                graphics.Clear(palette.Background);
                // Two radius-4 passes at 1/(4*dpi), plus bicubic support, cannot reach
                // beyond this margin. Keep the full-width material and exact sampling grid,
                // but skip expensive interpolation where only uniform background can exist.
                int reach = (int)Math.Ceiling(48 * dpi);
                graphics.SetClip(Rectangle.Intersect(bar.ClientRectangle,
                    new Rectangle(-origin.X - reach, 0, content.Width + reach * 2, bar.Height)));
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using ImageAttributes edges = new();
                edges.SetWrapMode(WrapMode.TileFlipXY);
                graphics.DrawImage(sample!, new Rectangle(-padding, -padding, area.Width, area.Height),
                    0, 0, sample!.Width, sample.Height, GraphicsUnit.Pixel, edges);
                using SolidBrush tint = new(Color.FromArgb(164, palette.Background));
                graphics.FillRectangle(tint, bar.ClientRectangle);
            }
            UpscaleTicks += Stopwatch.GetTimestamp() - stage; stage = Stopwatch.GetTimestamp();
            materialPixels = ReadPixels(material!, materialPixels);
            if (livePixels.Length != materialPixels.Length) livePixels = new int[materialPixels.Length];
            uint matte = BackdropSnapshot.RemovedMatte(palette);
            for (int i = 0; i < materialPixels.Length; i++)
                livePixels[i] = (int)BackdropSnapshot.RemoveMatte((uint)materialPixels[i] | 0xFF000000, matte);
            AlphaTicks += Stopwatch.GetTimestamp() - stage; Builds++;
            (sampled, pending) = (pending, sampled);
            sampleDpi = dpi;
            sampleValid = true;
            dirty = false;
        }
        finally { sampling = false; }
    }

    private static void PrintContent(Control control, Point origin, Rectangle parentClip, Rectangle area, nint dc)
    {
        if (!control.Visible) return;
        Rectangle bounds = new(origin, control.ClientSize);
        Rectangle clip = Rectangle.Intersect(Rectangle.Intersect(bounds, parentClip), area);
        if (clip.IsEmpty) return;
        int saved = SaveDC(dc);
        try
        {
            IntersectClipRect(dc, clip.Left - area.Left, clip.Top - area.Top, clip.Right - area.Left, clip.Bottom - area.Top);
            SetViewportOrgEx(dc, origin.X - area.X, origin.Y - area.Y, out _);
            SetTextColor(dc, ColorRef(control.ForeColor));
            SetBkColor(dc, ColorRef(control.BackColor));
            SendMessage(control.Handle, 0x318, dc, (nint)(4 | 8)); // WM_PRINTCLIENT, original painting only.
        }
        finally { RestoreDC(dc, saved); }
        // Print only intersecting children, rather than capturing the entire settings page per frame.
        foreach (Control child in control.Controls.Cast<Control>().Reverse())
            PrintContent(child, new Point(origin.X + child.Left, origin.Y + child.Top), clip, area, dc);
    }

    private void Composite(Control control, int[] original, int[] pixels, Size size, int[]? coverage, bool live)
    {
        EnsureMaterial();
        if (material == null || materialPixels.Length == 0) return;
        // Bitmap dimensions call GDI+; read them once rather than for every output pixel.
        int materialWidth = material.Width, materialHeight = material.Height;
        Point location = bar.PointToClient(control.PointToScreen(Point.Empty));
        int key = palette.Background.ToArgb() & 0xFFFFFF;
        uint matte = BackdropSnapshot.RemovedMatte(palette);
        for (int y = 0; y < size.Height; y++)
        for (int x = 0; x < size.Width; x++)
        {
            int at = y * size.Width + x, mx = location.X + x, my = location.Y + y;
            int pixel = original[at];
            uint amount = coverage != null ? BackdropSnapshot.Coverage((uint)coverage[at]) :
                (pixel & 0xFFFFFF) == key ? 255u : 0;
            if (amount == 0) { pixels[at] = pixel | unchecked((int)0xFF000000); continue; }
            if (amount > 0 && mx >= 0 && my >= 0 && mx < materialWidth && my < materialHeight)
            {
                if (amount == 255)
                {
                    pixels[at] = (live ? livePixels : materialPixels)[my * materialWidth + mx];
                    continue;
                }
                int background = materialPixels[my * materialWidth + mx];
                int r = Math.Clamp(((pixel >> 16) & 255) + (int)((((background >> 16) & 255) - palette.Background.R) * amount / 255), 0, 255);
                int g = Math.Clamp(((pixel >> 8) & 255) + (int)((((background >> 8) & 255) - palette.Background.G) * amount / 255), 0, 255);
                int b = Math.Clamp((pixel & 255) + (int)(((background & 255) - palette.Background.B) * amount / 255), 0, 255);
                pixel = r << 16 | g << 8 | b;
            }
            uint color = (uint)pixel | 0xFF000000;
            // Apply exactly the existing window material's matte, including each button's
            // original animated coverage. A new background color must not become an opaque patch.
            pixels[at] = (int)(live ? BackdropSnapshot.RemoveMatte(color, BackdropSnapshot.CoveredMatte(matte, amount)) : color);
        }
    }

    private void CopyBackground(Control control, int[] pixels, Size size, bool live)
    {
        EnsureMaterial();
        Point location = bar.PointToClient(control.PointToScreen(Point.Empty));
        int fill = palette.Background.ToArgb();
        if (live) fill = (int)BackdropSnapshot.RemoveMatte((uint)fill, BackdropSnapshot.RemovedMatte(palette));
        Array.Fill(pixels, fill);
        if (material == null) return;
        int[] source = live ? livePixels : materialPixels;
        int left = Math.Max(0, -location.X), right = Math.Min(size.Width, material.Width - location.X);
        for (int y = Math.Max(0, -location.Y); y < Math.Min(size.Height, material.Height - location.Y) && right > left; y++)
            Array.Copy(source, (y + location.Y) * material.Width + location.X + left, pixels, y * size.Width + left, right - left);
    }

    private static void Resize(ref Bitmap? bitmap, Size size, PixelFormat format = PixelFormat.Format32bppArgb)
    {
        if (bitmap?.Size == size) return;
        bitmap?.Dispose();
        bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), format);
    }
    private static int[] ReadPixels(Bitmap bitmap, int[]? pixels = null)
    {
        if (pixels?.Length != bitmap.Width * bitmap.Height) pixels = new int[bitmap.Width * bitmap.Height];
        BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
        finally { bitmap.UnlockBits(data); }
        return pixels;
    }
    private static void WritePixels(Bitmap bitmap, int[] pixels)
    {
        BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
        finally { bitmap.UnlockBits(data); }
    }
    private static int ColorRef(Color color) => color.R | color.G << 8 | color.B << 16;

    internal static void PrintOverlay(Control bar, nint dc)
    {
        if (!bar.Visible || bar.Width <= 0 || bar.Height <= 0) return;
        int saved = SaveDC(dc);
        try
        {
            IntersectClipRect(dc, bar.Left, bar.Top, bar.Right, bar.Bottom);
            GetViewportOrgEx(dc, out Point origin);
            SetViewportOrgEx(dc, origin.X + bar.Left, origin.Y + bar.Top, out _);
            SendMessage(bar.Handle, 0x317, dc, (nint)(4 | 8 | 16));
        }
        finally { RestoreDC(dc, saved); }
    }

    private sealed class Surface : NativeWindow, IDisposable
    {
        private readonly SettingsBarBackdrop owner;
        private readonly Control control;
        private nint memoryDc, bitmap, previousBitmap, bits;
        private Size size;
        private int[] pixels = [], originalPixels = [], coverage = [];
        private bool painting, nativeDirty = true;
        internal Surface(SettingsBarBackdrop owner, Control control)
        {
            this.owner = owner;
            this.control = control;
            control.HandleCreated += Created;
            control.HandleDestroyed += Destroyed;
            control.TextChanged += NativeChanged;
            control.FontChanged += NativeChanged;
            control.BackColorChanged += NativeChanged;
            control.ForeColorChanged += NativeChanged;
            control.EnabledChanged += NativeChanged;
            control.Invalidated += Invalidated;
            if (control.IsHandleCreated) Created(null, EventArgs.Empty);
        }
        private void Created(object? sender, EventArgs e)
        {
            AssignHandle(control.Handle);
            // WindowBackdrop attaches when the page is parented. Install this strictly local
            // material after it, so its live alpha is not converted a second time.
            control.BeginInvoke((Action)(() =>
            {
                if (owner.disposed || control.IsDisposed || !control.IsHandleCreated) return;
                ReleaseHandle();
                AssignHandle(control.Handle);
                control.Invalidate();
            }));
        }
        private void Destroyed(object? sender, EventArgs e) { ReleaseHandle(); ReleaseSurface(); }
        private void NativeChanged(object? sender, EventArgs e) => nativeDirty = true;
        private void Invalidated(object? sender, InvalidateEventArgs e) { if (!owner.invalidating) nativeDirty = true; }
        protected override void WndProc(ref Message message)
        {
            bool live = message.Msg == 0xF && message.WParam == 0;
            bool print = message.Msg == 0x318 && message.WParam != 0;
            if ((!live && !print) || painting || !owner.enabled || owner.disposed ||
                control.ClientSize.Width <= 0 || control.ClientSize.Height <= 0 || !EnsureSurface())
            {
                base.WndProc(ref message);
                return;
            }
            painting = true;
            nint target = message.WParam;
            PaintStruct paint = default;
            if (live) target = BeginPaint(message.HWnd, out paint);
            try
            {
                if (live && GetClipBox(target, out _) == 1) return;
                bool button = CaptureButtonPainter.HasBackdrop(control);
                // These bars contain plain layout panels, labels and the existing buttons.
                // Layout panels have no foreground; static text is captured only when it changes.
                if (control is Panel) owner.CopyBackground(control, pixels, size, live);
                else
                {
                    if (nativeDirty)
                    {
                        using (Graphics graphics = Graphics.FromHdc(memoryDc)) graphics.Clear(control.BackColor);
                        SetTextColor(memoryDc, ColorRef(control.ForeColor));
                        SetBkColor(memoryDc, ColorRef(control.BackColor));
                        Message original = Message.Create(message.HWnd, 0x318, memoryDc, (nint)(4 | 8));
                        base.WndProc(ref original);
                        GdiFlush();
                        Marshal.Copy(bits, originalPixels, 0, pixels.Length);
                        if (button)
                        {
                            using (Graphics graphics = Graphics.FromHdc(memoryDc)) CaptureButtonPainter.PaintBackdrop(control, graphics);
                            GdiFlush();
                            Marshal.Copy(bits, coverage, 0, coverage.Length);
                        }
                        nativeDirty = false;
                    }
                    owner.Composite(control, originalPixels, pixels, size, button ? coverage : null, live);
                }
                Marshal.Copy(pixels, 0, bits, pixels.Length);
                BitBlt(target, 0, 0, size.Width, size.Height, memoryDc, 0, 0, 0x00CC0020);
                message.Result = 0;
            }
            finally { if (live) EndPaint(message.HWnd, ref paint); painting = false; }
        }
        private bool EnsureSurface()
        {
            if (size == control.ClientSize && memoryDc != 0) return true;
            ReleaseSurface();
            BitmapInfo info = new() { Size = 40, Width = control.ClientSize.Width, Height = -control.ClientSize.Height, Planes = 1, BitCount = 32 };
            memoryDc = CreateCompatibleDC(0);
            if (memoryDc != 0) bitmap = CreateDIBSection(memoryDc, ref info, 0, out bits, 0, 0);
            if (bitmap == 0 || bits == 0) { ReleaseSurface(); return false; }
            previousBitmap = SelectObject(memoryDc, bitmap);
            if (previousBitmap == 0 || previousBitmap == (nint)(-1))
            {
                previousBitmap = 0;
                ReleaseSurface();
                return false;
            }
            size = control.ClientSize;
            pixels = new int[size.Width * size.Height];
            originalPixels = new int[pixels.Length];
            coverage = new int[pixels.Length];
            nativeDirty = true;
            return true;
        }
        private void ReleaseSurface()
        {
            if (previousBitmap != 0 && memoryDc != 0) SelectObject(memoryDc, previousBitmap);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memoryDc != 0) DeleteDC(memoryDc);
            memoryDc = bitmap = previousBitmap = bits = 0;
            size = default;
            pixels = originalPixels = coverage = [];
        }
        internal void ApplySnapshot(BackdropSnapshot snapshot, Point origin, Rectangle area, int key)
        {
            if (size != control.ClientSize || area.IsEmpty) return;
            bool panel = control is Panel, button = CaptureButtonPainter.HasBackdrop(control);
            for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                int at = (y - origin.Y) * size.Width + x - origin.X;
                uint amount = panel ? 255u : button ? BackdropSnapshot.Coverage((uint)coverage[at]) :
                    (originalPixels[at] & 0xFFFFFF) == key ? 255u : 0;
                snapshot.Pixels[y * snapshot.Size.Width + x] = BackdropSnapshot.CoveredMatte(snapshot.Background, amount);
            }
        }
        public void Dispose()
        {
            control.HandleCreated -= Created;
            control.HandleDestroyed -= Destroyed;
            control.TextChanged -= NativeChanged;
            control.FontChanged -= NativeChanged;
            control.BackColorChanged -= NativeChanged;
            control.ForeColorChanged -= NativeChanged;
            control.EnabledChanged -= NativeChanged;
            control.Invalidated -= Invalidated;
            ReleaseHandle();
            ReleaseSurface();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach ((Control control, Surface surface) in surfaces)
        {
            control.ControlAdded -= BarAdded;
            control.LocationChanged -= Changed;
            control.SizeChanged -= Changed;
            surface.Dispose();
        }
        foreach (Control source in sources)
        {
            source.Invalidated -= SourceInvalidated;
            source.TextChanged -= SourceChanged;
            source.FontChanged -= SourceChanged;
            source.BackColorChanged -= SourceChanged;
            source.ForeColorChanged -= SourceChanged;
            source.EnabledChanged -= SourceChanged;
            source.LocationChanged -= Changed;
            source.SizeChanged -= Changed;
            source.ControlAdded -= SourceAdded;
        }
        surfaces.Clear(); sources.Clear(); sourceVersions.Clear(); sampled.Clear(); pending.Clear();
        capture?.Dispose(); sample?.Dispose(); material?.Dispose();
    }

    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
    [DllImport("gdi32.dll")] private static extern int SaveDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool RestoreDC(nint dc, int saved);
    [DllImport("gdi32.dll")] private static extern int IntersectClipRect(nint dc, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool SetViewportOrgEx(nint dc, int x, int y, out Point previous);
    [DllImport("gdi32.dll")] private static extern bool GetViewportOrgEx(nint dc, out Point origin);
    [DllImport("gdi32.dll")] private static extern int SetTextColor(nint dc, int color);
    [DllImport("gdi32.dll")] private static extern int SetBkColor(nint dc, int color);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PaintStruct
    {
        public nint Dc; public int Erase; public NativeRect Rect; public int Restore, IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, ImageSize; public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant;
    }
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint window, out PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint window, ref PaintStruct paint);
    [DllImport("gdi32.dll")] private static extern int GetClipBox(nint dc, out NativeRect rectangle);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
}
