// GPL-3.0-or-later. Frost only the two fixed settings bars using the scrolling content beneath them.
using ShareX.HelpersLib;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Numerics;

namespace ShareX.Roll;

internal interface IBackdropSnapshotOverlay
{
    void ApplySnapshotOverlay(BackdropSnapshot snapshot);
}

internal sealed class SettingsBarBackdrop : IDisposable
{
    private readonly Control bar, content;
    private readonly SettingsScrollBackdropCache? scrollCache;
    private readonly bool clearAtBottom;
    private readonly Dictionary<Control, Surface> surfaces = new();
    private readonly HashSet<Control> sources = new();
    private readonly Dictionary<Control, int> sourceVersions = new();
    private List<SampleState> sampled = [], pending = [];
    private bool sampleValid;
    private float sampleDpi;
    internal long CaptureTicks, FilterTicks, UpscaleTicks, AlphaTicks;
    internal int Builds, Reuses;
    internal int SurfaceComposites, SurfaceReuses;
    private int materialVersion;
    private readonly MaterialBuffer opaqueBuffer = new(), glassBuffer = new();
    private readonly record struct SampleState(Control Control, Rectangle Clip, Point Origin, Size Size, int Version, int Background);
    private CapturePalette palette = CapturePalette.Light;
    private Bitmap? capture, sample, material, clearEdge;
    private int[] materialPixels = [], livePixels = [], clearPixels = [];
    private int[] clearMattes = [], edgeMattes = [];
    private bool enabled, dirty = true, sampling, invalidating, disposed;

    internal SettingsBarBackdrop(Control bar, Control content, SettingsScrollBackdropCache? scrollCache = null, bool clearAtBottom = false)
    {
        this.bar = bar;
        this.content = content;
        this.scrollCache = scrollCache;
        this.clearAtBottom = clearAtBottom;
        if (scrollCache != null) scrollCache.Ready += CacheReady;
        ObserveBar(bar);
        ObserveSource(content.Parent ?? content);
    }

    internal void Apply(CapturePalette value, bool active)
    {
        palette = value;
        enabled = active && !SystemInformation.HighContrast;
        scrollCache?.Apply(value, enabled);
        sampleValid = false;
        dirty = false;
        Changed(null, EventArgs.Empty);
    }

    private void CacheReady(object? sender, EventArgs e)
    {
        sampleValid = false;
        Changed(sender, e);
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
            // Check the source sample on the bar first. Unchanged material does not
            // require repainting every foreground label and button in the fixed bar.
            try { bar.Invalidate(); }
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
            Rectangle observedArea = area;
            // The shared scene also includes blur contributions beyond the local
            // capture margin. Observe that complete support before reusing a crop.
            if (scrollCache != null) observedArea.Inflate(padding, padding);
            CollectSample(content, Point.Empty, new Rectangle(Point.Empty, content.ClientSize), observedArea);
            if (sampleValid && sampleDpi == dpi && material?.Size == bar.ClientSize && pending.SequenceEqual(sampled))
            {
                Reuses++;
                dirty = false;
                return;
            }
            if (scrollCache != null)
            {
                Resize(ref material, bar.ClientSize);
                long copied = Stopwatch.GetTimestamp();
                bool cached;
                using (Graphics graphics = Graphics.FromImage(material!)) cached = scrollCache.Draw(graphics, origin, bar.ClientSize);
                if (cached)
                {
                    PrepareClearEdge(origin, dpi, cached: true);
                    UpscaleTicks += Stopwatch.GetTimestamp() - copied;
                    CommitMaterial(dpi);
                    return;
                }
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
                // The sampled backdrop is opaque. Copy it directly, then apply
                // the original tint; source-over blending the enlarged image is redundant.
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImage(sample!, new Rectangle(-padding, -padding, area.Width, area.Height),
                    0, 0, sample!.Width, sample.Height, GraphicsUnit.Pixel, edges);
                graphics.CompositingMode = CompositingMode.SourceOver;
                using SolidBrush tint = new(Color.FromArgb(164, palette.Background));
                graphics.FillRectangle(tint, bar.ClientRectangle);
            }
            PrepareClearEdge(origin, dpi, cached: false);
            UpscaleTicks += Stopwatch.GetTimestamp() - stage; stage = Stopwatch.GetTimestamp();
            CommitMaterial(dpi);
        }
        finally { sampling = false; }
    }

    private void PrepareClearEdge(Point origin, float dpi, bool cached)
    {
        // Feather only the inner edge. Keep the foreground controls over the
        // original material, with no extra blur pass or per-frame scene capture.
        int height = Math.Min(bar.Height, (int)Math.Ceiling(32 * dpi));
        int top = clearAtBottom ? bar.Height - height : 0;
        Resize(ref clearEdge, new Size(bar.Width, height));
        if (clearMattes.Length != bar.Width * height) clearMattes = new int[bar.Width * height];
        if (cached)
        {
            using Graphics graphics = Graphics.FromImage(clearEdge!);
            Point source = new(origin.X, origin.Y + top);
            scrollCache!.Draw(graphics, source, clearEdge!.Size, sharp: true);
            scrollCache.CopySharpMatte(source, clearEdge.Size, clearMattes);
        }
        else
        {
            int padding = (int)Math.Ceiling(24 * dpi);
            using (Graphics graphics = Graphics.FromImage(clearEdge!))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImage(capture!, new Rectangle(Point.Empty, clearEdge!.Size),
                    padding, padding + top, clearEdge.Width, height, GraphicsUnit.Pixel);
            }
            BackdropSnapshot coverage = BackdropSnapshot.Capture(content, clearEdge!, palette, new Point(-origin.X, -origin.Y - top));
            MemoryMarshal.Cast<uint, int>(coverage.Pixels.AsSpan()).CopyTo(clearMattes);
        }
    }

    private void CommitMaterial(float dpi)
    {
        long stage = Stopwatch.GetTimestamp();
        materialPixels = ReadPixels(material!, materialPixels);
        clearPixels = ReadPixels(clearEdge!, clearPixels);
        BlendScrollEdge(materialPixels, clearPixels, bar.Width, bar.Height, clearAtBottom);
        WritePixels(material!, materialPixels);
        if (livePixels.Length != materialPixels.Length) livePixels = new int[materialPixels.Length];
        uint matte = BackdropSnapshot.RemovedMatte(palette);
        RemoveMaterialMatte(materialPixels, livePixels, matte);
        if (edgeMattes.Length != clearMattes.Length) edgeMattes = new int[clearMattes.Length];
        Array.Fill(edgeMattes, (int)matte);
        BlendScrollEdge(edgeMattes, clearMattes, bar.Width, clearEdge!.Height, clearAtBottom, blendAlpha: true);
        int edgeTop = clearAtBottom ? bar.Height - clearEdge.Height : 0;
        materialPixels.AsSpan(edgeTop * bar.Width, edgeMattes.Length).CopyTo(livePixels.AsSpan(edgeTop * bar.Width));
        BackdropSnapshot.RemoveMatte(MemoryMarshal.Cast<int, uint>(livePixels.AsSpan(edgeTop * bar.Width, edgeMattes.Length)),
            MemoryMarshal.Cast<int, uint>(edgeMattes.AsSpan()));
        AlphaTicks += Stopwatch.GetTimestamp() - stage; Builds++;
        materialVersion++;
        invalidating = true;
        try
        {
            foreach (Control child in surfaces.Keys)
                if (child != bar && child.Visible) child.Invalidate();
        }
        finally { invalidating = false; }
        (sampled, pending) = (pending, sampled);
        sampleDpi = dpi;
        sampleValid = true;
        dirty = false;
    }

    // Blend the already filtered scene into its original pixels with a smoothstep
    // mask. The mask follows bar coordinates, so child panels share the same fade.
    internal static void BlendScrollEdge(Span<int> material, ReadOnlySpan<int> sharp, int width, int height, bool clearAtBottom, bool blendAlpha = false)
    {
        int rows = sharp.Length / width, top = clearAtBottom ? height - rows : 0;
        Vector<uint> mask = new(255), alpha = new(0xFF000000);
        for (int row = 0; row < rows; row++)
        {
            double distance = clearAtBottom ? rows - 1 - row : row;
            double t = rows <= 1 ? 0 : distance / (rows - 1);
            uint amount = (uint)Math.Round(256 * (1 - t * t * (3 - 2 * t)));
            if (amount == 0) continue;
            Span<uint> target = MemoryMarshal.Cast<int, uint>(material.Slice((top + row) * width, width));
            ReadOnlySpan<uint> source = MemoryMarshal.Cast<int, uint>(sharp.Slice(row * width, width));
            Vector<uint> clear = new(amount), blur = new(256 - amount);
            int x = 0;
            if (Vector.IsHardwareAccelerated)
                for (; x <= width - Vector<uint>.Count; x += Vector<uint>.Count)
                {
                    Vector<uint> a = new(target.Slice(x)), b = new(source.Slice(x));
                    Vector<uint> r = Vector.ShiftRightLogical((Vector.ShiftRightLogical(a, 16) & mask) * blur + (Vector.ShiftRightLogical(b, 16) & mask) * clear, 8);
                    Vector<uint> g = Vector.ShiftRightLogical((Vector.ShiftRightLogical(a, 8) & mask) * blur + (Vector.ShiftRightLogical(b, 8) & mask) * clear, 8);
                    Vector<uint> blue = Vector.ShiftRightLogical((a & mask) * blur + (b & mask) * clear, 8);
                    Vector<uint> opacity = blendAlpha ? Vector.ShiftLeft(Vector.ShiftRightLogical(Vector.ShiftRightLogical(a, 24) * blur + Vector.ShiftRightLogical(b, 24) * clear, 8), 24) : alpha;
                    (opacity | Vector.ShiftLeft(r, 16) | Vector.ShiftLeft(g, 8) | blue).CopyTo(target.Slice(x));
                }
            for (; x < width; x++)
            {
                uint a = target[x], b = source[x], inverse = 256 - amount;
                uint r = (((a >> 16) & 255) * inverse + ((b >> 16) & 255) * amount) >> 8;
                uint g = (((a >> 8) & 255) * inverse + ((b >> 8) & 255) * amount) >> 8;
                uint blue = ((a & 255) * inverse + (b & 255) * amount) >> 8;
                uint opacity = blendAlpha ? (((a >> 24) * inverse + (b >> 24) * amount) >> 8) << 24 : 0xFF000000;
                target[x] = opacity | r << 16 | g << 8 | blue;
            }
        }
    }

    private uint MaterialMatteAt(int x, int y)
    {
        int rows = edgeMattes.Length / Math.Max(1, bar.Width), top = clearAtBottom ? bar.Height - rows : 0;
        return x >= 0 && x < bar.Width && y >= top && y < top + rows
            ? (uint)edgeMattes[(y - top) * bar.Width + x] : BackdropSnapshot.RemovedMatte(palette);
    }

    internal static void RemoveMaterialMatte(ReadOnlySpan<int> source, Span<int> destination, uint matte)
    {
        ReadOnlySpan<uint> input = MemoryMarshal.Cast<int, uint>(source);
        Span<uint> output = MemoryMarshal.Cast<int, uint>(destination);
        Vector<uint> mask = new(255), redMatte = new((matte >> 16) & 255),
            greenMatte = new((matte >> 8) & 255), blueMatte = new(matte & 255), alpha = new((255 - (matte >> 24)) << 24);
        int i = 0;
        if (Vector.IsHardwareAccelerated)
            for (; i <= input.Length - Vector<uint>.Count; i += Vector<uint>.Count)
            {
                Vector<uint> color = new(input.Slice(i));
                Vector<uint> red = Vector.ShiftRightLogical(color, 16) & mask, green = Vector.ShiftRightLogical(color, 8) & mask, blue = color & mask;
                red -= Vector.Min(red, redMatte);
                green -= Vector.Min(green, greenMatte);
                blue -= Vector.Min(blue, blueMatte);
                (alpha | Vector.ShiftLeft(red, 16) | Vector.ShiftLeft(green, 8) | blue).CopyTo(output.Slice(i));
            }
        for (; i < input.Length; i++) output[i] = BackdropSnapshot.RemoveMatte(input[i] | 0xFF000000, matte);
    }

    internal static void PrintContent(Control control, Point origin, Rectangle parentClip, Rectangle area, nint dc)
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
            if (BackgroundOnly(control))
            {
                nint brush = CreateSolidBrush(ColorRef(control.BackColor));
                try
                {
                    NativeRect background = new() { Right = control.ClientSize.Width, Bottom = control.ClientSize.Height };
                    FillRect(dc, ref background, brush);
                }
                finally { DeleteObject(brush); }
            }
            else SendMessage(control.Handle, 0x318, dc, (nint)(4 | 8)); // WM_PRINTCLIENT, original painting only.
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
            pixels[at] = (int)(live ? BackdropSnapshot.RemoveMatte(color, BackdropSnapshot.CoveredMatte(MaterialMatteAt(mx, my), amount)) : color);
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

    private bool PaintBackground(Control control, nint dc, bool live)
    {
        if (material == null) return false;
        Point location = bar.PointToClient(control.PointToScreen(Point.Empty));
        Rectangle source = new(location, control.ClientSize);
        if (!bar.ClientRectangle.Contains(source)) return false;
        MaterialBuffer buffer = live ? glassBuffer : opaqueBuffer;
        if (!buffer.Update(material.Size, live ? livePixels : materialPixels, materialVersion)) return false;
        return BitBlt(dc, 0, 0, source.Width, source.Height, buffer.Dc, source.X, source.Y, 0x00CC0020);
    }

    // Layout panels share the bar's already composited pixels. Upload once per
    // material revision, then let GDI copy each panel's clipped region directly.
    private sealed class MaterialBuffer : IDisposable
    {
        internal nint Dc;
        private nint bitmap, previous, bits;
        private Size size;
        private int version = -1;

        internal bool Update(Size value, int[] pixels, int revision)
        {
            if (size != value || Dc == 0)
            {
                Dispose();
                BitmapInfo info = new() { Size = 40, Width = value.Width, Height = -value.Height, Planes = 1, BitCount = 32 };
                Dc = CreateCompatibleDC(0);
                if (Dc != 0) bitmap = CreateDIBSection(Dc, ref info, 0, out bits, 0, 0);
                if (bitmap == 0 || bits == 0) { Dispose(); return false; }
                previous = SelectObject(Dc, bitmap);
                if (previous == 0 || previous == (nint)(-1)) { previous = 0; Dispose(); return false; }
                size = value;
            }
            if (version != revision)
            {
                GdiFlush();
                Marshal.Copy(pixels, 0, bits, pixels.Length);
                version = revision;
            }
            return true;
        }

        public void Dispose()
        {
            if (previous != 0 && Dc != 0) SelectObject(Dc, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            if (Dc != 0) DeleteDC(Dc);
            Dc = bitmap = previous = bits = 0;
            size = default;
            version = -1;
        }
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
        private int paintedVersion = -1;
        private Point paintedLocation;
        private bool paintedLive;
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
                owner.EnsureMaterial();
                if (control is Panel && owner.PaintBackground(control, target, live))
                {
                    owner.SurfaceReuses++;
                    message.Result = 0;
                    return;
                }
                Point location = owner.bar.PointToClient(control.PointToScreen(Point.Empty));
                bool composite = paintedVersion != owner.materialVersion || paintedLocation != location || paintedLive != live;
                bool button = CaptureButtonPainter.HasBackdrop(control);
                // These bars contain plain layout panels, labels and the existing buttons.
                // Layout panels have no foreground; static text is captured only when it changes.
                if (control is Panel)
                {
                    if (composite) owner.CopyBackground(control, pixels, size, live);
                }
                else
                {
                    if (nativeDirty)
                    {
                        composite = true;
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
                    if (composite) owner.Composite(control, originalPixels, pixels, size, button ? coverage : null, live);
                }
                if (composite)
                {
                    Marshal.Copy(pixels, 0, bits, pixels.Length);
                    paintedVersion = owner.materialVersion;
                    paintedLocation = location;
                    paintedLive = live;
                    owner.SurfaceComposites++;
                }
                else owner.SurfaceReuses++;
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
            paintedVersion = -1;
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
            Point location = owner.bar.PointToClient(control.PointToScreen(Point.Empty));
            if (panel)
            {
                int rows = owner.edgeMattes.Length / owner.bar.Width;
                int top = owner.clearAtBottom ? owner.bar.Height - rows : 0;
                int sourceX = location.X + area.Left - origin.X;
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    Span<uint> output = snapshot.Pixels.AsSpan(y * snapshot.Size.Width + area.Left, area.Width);
                    int sourceY = location.Y + y - origin.Y;
                    if (sourceY >= top && sourceY < top + rows)
                        MemoryMarshal.Cast<int, uint>(owner.edgeMattes.AsSpan((sourceY - top) * owner.bar.Width + sourceX, area.Width)).CopyTo(output);
                    else output.Fill(snapshot.Background);
                }
                return;
            }
            for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                int at = (y - origin.Y) * size.Width + x - origin.X;
                uint amount = button ? BackdropSnapshot.Coverage((uint)coverage[at]) :
                    (originalPixels[at] & 0xFFFFFF) == key ? 255u : 0;
                snapshot.Pixels[y * snapshot.Size.Width + x] = BackdropSnapshot.CoveredMatte(
                    owner.MaterialMatteAt(location.X + x - origin.X, location.Y + y - origin.Y), amount);
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
        if (scrollCache != null) scrollCache.Ready -= CacheReady;
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
        capture?.Dispose(); sample?.Dispose(); material?.Dispose(); clearEdge?.Dispose();
        opaqueBuffer.Dispose(); glassBuffer.Dispose();
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
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(int color);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref NativeRect rectangle, nint brush);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
}
