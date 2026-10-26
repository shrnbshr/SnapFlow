// GPL-3.0-or-later. DWM owns the Acrylic material; existing controls own layout, input and animation.
using Microsoft.Win32;
using ShareX.HelpersLib;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ShareX.Roll;

// Opt in only for solid settings containers with no border/image/foreground of their own.
internal interface IBackdropBackground { }
// Only this scope opts static labels and standard borderless layout panels into caching.
internal interface IBackdropCachingScope { }

internal sealed class WindowBackdrop : IDisposable
{
    private readonly Form form;
    private readonly Func<Rectangle>? opaqueBounds;
    private readonly Dictionary<Control, PaintWindow> windows = new();
    private readonly ConditionalWeakTable<Bitmap, BackdropSnapshot> snapshots = new();
    private readonly Dictionary<PageTransition, TransitionMaterial> transitions = new();
    private CapturePalette palette = CapturePalette.Light;
    private bool disposed;
    private bool enabled = true;
    private bool applied, extended;
    internal bool IsActive { get; private set; }

    private sealed class TransitionMaterial(BackdropSnapshot before)
    {
        internal readonly BackdropSnapshot Before = before;
        internal BackdropSnapshot? After;
        internal readonly BackdropSnapshot Surface = new(before.Size, before.Background);
        internal BackdropSnapshot Render(PageTransition transition)
        {
            var placement = transition.MaterialPlacement;
            Surface.Compose(Before, placement.BeforeX, After, placement.AfterX, placement.Opacity);
            return Surface;
        }
    }

    internal void RememberSnapshot(Control page, Bitmap bitmap)
    {
        if (!IsActive) return;
        snapshots.Add(bitmap, page is PageTransition transition && transitions.TryGetValue(transition, out TransitionMaterial? material)
            ? material.Render(transition).Copy() : BackdropSnapshot.Capture(page, bitmap, palette));
    }

    internal void RememberOpaqueSnapshot(Bitmap bitmap)
    {
        if (IsActive) snapshots.Add(bitmap, new BackdropSnapshot(bitmap.Size, 0));
    }

    internal void CopySnapshot(Bitmap source, Bitmap destination)
    {
        if (snapshots.TryGetValue(source, out BackdropSnapshot? snapshot)) snapshots.Add(destination, snapshot);
    }

    internal void BeginTransition(PageTransition transition, Bitmap before)
    {
        if (snapshots.TryGetValue(before, out BackdropSnapshot? snapshot)) transitions[transition] = new(snapshot);
    }

    internal void CompleteTransition(PageTransition transition, Bitmap after)
    {
        if (transitions.TryGetValue(transition, out TransitionMaterial? material) && snapshots.TryGetValue(after, out BackdropSnapshot? snapshot))
            material.After = snapshot;
    }

    internal WindowBackdrop(Form form, Func<Rectangle>? opaqueBounds = null)
    {
        this.form = form;
        this.opaqueBounds = opaqueBounds;
        Observe(form);
        form.Disposed += FormDisposed;
    }

    internal void Apply(CapturePalette value, bool enabled = true)
    {
        this.enabled = enabled;
        bool changed = palette != value;
        palette = value;
        if (disposed || !form.IsHandleCreated) return;
        bool supported = IsSupported();
        bool active = enabled && supported;
        // Keep the client redirection surface stable across app-level toggles.
        // The opaque mode still paints alpha=255; only the material and pixel coverage change.
        if (active != IsActive || !applied || extended != supported)
        {
            changed = true;
            int material = active ? 3 : 1; // DWMSBT_TRANSIENTWINDOW / DWMSBT_NONE.
            MARGINS margins = supported ? new() { leftWidth = -1, rightWidth = -1, topHeight = -1, bottomHeight = -1 } : default;
            bool materialReady = NativeMethods.DwmSetWindowAttribute(form.Handle, 38, ref material, sizeof(int)) >= 0;
            bool frameReady = (applied && extended == supported) ||
                NativeMethods.DwmExtendFrameIntoClientArea(form.Handle, ref margins) >= 0;
            extended = supported && frameReady;
            if (!materialReady || !frameReady)
            {
                active = extended = false;
                material = 1; margins = default;
                NativeMethods.DwmSetWindowAttribute(form.Handle, 38, ref material, sizeof(int));
                NativeMethods.DwmExtendFrameIntoClientArea(form.Handle, ref margins);
            }
            applied = true;
        }
        IsActive = active;
        if (!extended) foreach (PaintWindow window in windows.Values) window.ReleaseSurface();
        if (changed)
        {
            form.Invalidate(true);
            if (form.Visible) RedrawWindow(form.Handle, 0, 0, 0x0100 | 0x0080);
        }
    }

    internal static bool IsSupported()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) || SystemInformation.HighContrast) return false;
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "EnableTransparency", 1) is not int enabled || enabled != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(nint window, nint updateRectangle, nint updateRegion, uint flags);

    private bool AllowsGlass(Control control)
    {
        // A screenshot can contain any color, including the page background and pure black.
        // The rounded preview frame's outside corners expose the material; its children stay opaque.
        if (control is PreviewSurface) return true;
        for (Control? ancestor = control; ancestor != null; ancestor = ancestor.Parent)
            if (ancestor is PreviewSurface or PictureBox) return false;
        return control is not PageTransition;
    }

    private void Observe(Control control)
    {
        if (windows.ContainsKey(control)) return;
        PaintWindow window = new(this, control);
        windows.Add(control, window);
        control.ControlAdded += ControlAdded;
        control.ControlRemoved += ControlRemoved;
        control.Disposed += ControlDisposed;
        foreach (Control child in control.Controls) Observe(child);
    }

    private void ControlAdded(object? sender, ControlEventArgs e) { if (e.Control != null) Observe(e.Control); }
    private void ControlRemoved(object? sender, ControlEventArgs e) { if (e.Control != null) Forget(e.Control); }
    private void ControlDisposed(object? sender, EventArgs e) { if (sender is Control control) Forget(control); }
    private void FormDisposed(object? sender, EventArgs e) => Dispose();

    private void Forget(Control control)
    {
        foreach (Control child in control.Controls) Forget(child);
        control.ControlAdded -= ControlAdded;
        control.ControlRemoved -= ControlRemoved;
        control.Disposed -= ControlDisposed;
        if (windows.Remove(control, out PaintWindow? window)) window.Dispose();
        if (control is PageTransition transition) transitions.Remove(transition);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        form.Disposed -= FormDisposed;
        Forget(form);
        // Disposed children can already have been detached from the form.
        foreach (Control control in windows.Keys.ToArray()) Forget(control);
    }

    private sealed class PaintWindow : NativeWindow, IDisposable
    {
        private readonly WindowBackdrop owner;
        private readonly Control control;
        private IntPtr memoryDc, bitmap, previousBitmap, bits;
        private Size size;
        private int[] pixels = [];
        private int[] buttonCoverage = [];
        private bool painting;
        private int? cachedBackground;
        private readonly bool backgroundOnly, cacheLabel;
        private bool pixelsDirty = true;
        private CapturePalette? paintedPalette;
        private bool paintedActive;

        internal PaintWindow(WindowBackdrop owner, Control control)
        {
            this.owner = owner;
            this.control = control;
            bool scoped = false;
            for (Control? ancestor = control; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor is IBackdropCachingScope) { scoped = true; break; }
            Type type = control.GetType();
            backgroundOnly = control is IBackdropBackground || scoped && control is Panel { BorderStyle: BorderStyle.None, BackgroundImage: null } &&
                (type == typeof(Panel) || type == typeof(FlowLayoutPanel) ||
                    type == typeof(TableLayoutPanel) && ((TableLayoutPanel)control).CellBorderStyle == TableLayoutPanelCellBorderStyle.None);
            cacheLabel = scoped && type == typeof(Label);
            if (cacheLabel)
            {
                control.Invalidated += PixelsInvalidated;
                control.TextChanged += PixelsChanged; control.FontChanged += PixelsChanged;
                control.BackColorChanged += PixelsChanged; control.ForeColorChanged += PixelsChanged;
                control.EnabledChanged += PixelsChanged;
            }
            control.HandleCreated += HandleCreated;
            control.HandleDestroyed += HandleDestroyed;
            if (control.IsHandleCreated) AssignHandle(control.Handle);
        }

        private void HandleCreated(object? sender, EventArgs e) => AssignHandle(control.Handle);
        private void PixelsChanged(object? sender, EventArgs e) => pixelsDirty = true;
        private void PixelsInvalidated(object? sender, InvalidateEventArgs e) => pixelsDirty = true;
        private void HandleDestroyed(object? sender, EventArgs e)
        {
            ReleaseHandle();
            ReleaseSurface();
            if (control == owner.form) { owner.IsActive = false; owner.applied = owner.extended = false; }
        }

        protected override void WndProc(ref Message message)
        {
            if (owner.extended && message.Msg == 0x14 && WindowFromDC(message.WParam) != IntPtr.Zero)
            {
                // A live background erase would expose opaque GDI pixels until the complete alpha frame arrives.
                // Keep erasing into memory DCs for WM_PRINT/DrawToBitmap, which still need their normal background.
                message.Result = new IntPtr(1);
                return;
            }
            if (message.Msg == 0x31E && control == owner.form) // WM_DWMCOMPOSITIONCHANGED.
            {
                base.WndProc(ref message);
                owner.IsActive = false;
                owner.applied = false;
                owner.Apply(owner.palette, owner.enabled);
                return;
            }
            if (!owner.extended || painting || message.Msg != 0xF || message.WParam != IntPtr.Zero
                || control.ClientSize.Width <= 0 || control.ClientSize.Height <= 0)
            {
                base.WndProc(ref message);
                return;
            }
            if (!EnsureSurface(backgroundOnly ? new Size(1, 1) : control.ClientSize))
            {
                owner.Apply(owner.palette, enabled: false);
                base.WndProc(ref message);
                return;
            }
            IntPtr dc = BeginPaint(message.HWnd, out PaintStruct paint);
            painting = true;
            try
            {
                if (GetClipBox(dc, out _) == 1) // NULLREGION: fully covered controls have no pixels to submit.
                {
                    message.Result = IntPtr.Zero;
                    return;
                }
                if (cacheLabel && !pixelsDirty && paintedPalette == owner.palette && paintedActive == owner.IsActive)
                {
                    BitBlt(dc, 0, 0, size.Width, size.Height, memoryDc, 0, 0, 0x00CC0020);
                    return;
                }
                if (backgroundOnly)
                {
                    uint color = (uint)control.BackColor.ToArgb() | 0xFF000000;
                    if (owner.IsActive && (color & 0xFFFFFF) == ((uint)owner.palette.Background.ToArgb() & 0xFFFFFF))
                        color = BackdropSnapshot.RemoveMatte(color, BackdropSnapshot.RemovedMatte(owner.palette));
                    if (cachedBackground != (int)color)
                    {
                        Array.Fill(pixels, (int)color);
                        Marshal.Copy(pixels, 0, bits, pixels.Length);
                        cachedBackground = (int)color;
                    }
                    // A constant background needs one cached pixel, not a new full-height
                    // bitmap every time the settings accordion changes its parent's size.
                    StretchBlt(dc, 0, 0, control.ClientSize.Width, control.ClientSize.Height,
                        memoryDc, 0, 0, 1, 1, 0x00CC0020);
                    return;
                }
                // WM_PRINTCLIENT uses each existing OnPaint implementation, including its current tween value.
                // WM_PRINT/DrawToBitmap remain untouched so snapshots and exported images retain their pixels.
                SendMessage(message.HWnd, 0x318, memoryDc, new IntPtr(4 | 8));
                GdiFlush(); // Finish queued GDI drawing before reading or changing DIB memory.
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                Span<uint> output = MemoryMarshal.Cast<int, uint>(pixels.AsSpan());
                uint background = (uint)owner.palette.Background.ToArgb() & 0xFFFFFF;
                uint matte = BackdropSnapshot.RemovedMatte(owner.palette);
                uint tint = BackdropSnapshot.RemoveMatte(background, matte);
                bool glass = owner.IsActive && owner.AllowsGlass(control);
                BackdropSnapshot? material = owner.IsActive && control is PageTransition transition && owner.transitions.TryGetValue(transition, out TransitionMaterial? state)
                    ? state.Render(transition) : null;
                int i = 0;
                EditorToolStripRenderer? editorRenderer = (control as ToolStrip)?.Renderer as EditorToolStripRenderer;
                if (glass && (CaptureButtonPainter.HasBackdrop(control) || editorRenderer != null))
                {
                    if (buttonCoverage.Length != pixels.Length) buttonCoverage = GC.AllocateUninitializedArray<int>(pixels.Length);
                    if (editorRenderer != null)
                    {
                        editorRenderer.PaintingCoverage = true;
                        try { SendMessage(message.HWnd, 0x318, memoryDc, new IntPtr(4 | 8)); }
                        finally { editorRenderer.PaintingCoverage = false; }
                    }
                    else
                        using (Graphics graphics = Graphics.FromHdc(memoryDc)) CaptureButtonPainter.PaintBackdrop(control, graphics);
                    GdiFlush();
                    Marshal.Copy(bits, buttonCoverage, 0, buttonCoverage.Length);
                    BackdropSnapshot.RemoveButtonMatte(output, buttonCoverage, matte);
                    i = output.Length;
                }
                if (material != null)
                {
                    BackdropSnapshot.RemoveMatte(output, material.Pixels);
                    i = output.Length;
                }
                Rectangle imageBounds = control == owner.form && owner.opaqueBounds != null
                    ? Rectangle.Intersect(control.ClientRectangle, owner.opaqueBounds()) : Rectangle.Empty;
                if (i == 0 && !imageBounds.IsEmpty)
                {
                    for (int y = 0; y < size.Height; y++)
                    {
                        Span<uint> row = output.Slice(y * size.Width, size.Width);
                        if (y < imageBounds.Top || y >= imageBounds.Bottom)
                            ConvertBackground(row, background, tint, glass);
                        else
                        {
                            ConvertBackground(row[..imageBounds.Left], background, tint, glass);
                            ConvertBackground(row[imageBounds.Left..imageBounds.Right], background, tint, glass: false);
                            ConvertBackground(row[imageBounds.Right..], background, tint, glass);
                        }
                    }
                    i = output.Length;
                }
                Vector<uint> rgb = new(0xFFFFFF), opaque = new(0xFF000000), key = new(background);
                if (material == null && i == 0 && Vector.IsHardwareAccelerated)
                    for (; i <= output.Length - Vector<uint>.Count; i += Vector<uint>.Count)
                    {
                        Vector<uint> source = new(output.Slice(i));
                        Vector<uint> value = source | opaque;
                        if (glass) value = Vector.ConditionalSelect(Vector.Equals(source & rgb, key), new Vector<uint>(tint), value);
                        value.CopyTo(output.Slice(i));
                    }
                for (; i < output.Length; i++)
                    output[i] = glass && (output[i] & 0xFFFFFF) == background ? tint : output[i] | 0xFF000000;
                Marshal.Copy(pixels, 0, bits, pixels.Length);
                if (cacheLabel) { pixelsDirty = false; paintedPalette = owner.palette; paintedActive = owner.IsActive; }
                BitBlt(dc, 0, 0, size.Width, size.Height, memoryDc, 0, 0, 0x00CC0020);
            }
            finally
            {
                painting = false;
                EndPaint(message.HWnd, ref paint);
            }
            message.Result = IntPtr.Zero;
        }

        private static void ConvertBackground(Span<uint> pixels, uint background, uint tint, bool glass)
        {
            Vector<uint> rgb = new(0xFFFFFF), opaque = new(0xFF000000), key = new(background);
            int i = 0;
            if (Vector.IsHardwareAccelerated)
                for (; i <= pixels.Length - Vector<uint>.Count; i += Vector<uint>.Count)
                {
                    Vector<uint> source = new(pixels.Slice(i));
                    Vector<uint> value = source | opaque;
                    if (glass) value = Vector.ConditionalSelect(Vector.Equals(source & rgb, key), new Vector<uint>(tint), value);
                    value.CopyTo(pixels.Slice(i));
                }
            for (; i < pixels.Length; i++)
                pixels[i] = glass && (pixels[i] & 0xFFFFFF) == background ? tint : pixels[i] | 0xFF000000;
        }

        private bool EnsureSurface(Size value)
        {
            if (size == value && memoryDc != IntPtr.Zero) return true;
            ReleaseSurface();
            BitmapInfo info = new() { Size = 40, Width = value.Width, Height = -value.Height, Planes = 1, BitCount = 32 };
            memoryDc = CreateCompatibleDC(IntPtr.Zero);
            if (memoryDc != IntPtr.Zero) bitmap = CreateDIBSection(memoryDc, ref info, 0, out bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) { ReleaseSurface(); return false; }
            previousBitmap = SelectObject(memoryDc, bitmap);
            if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1)) { previousBitmap = IntPtr.Zero; ReleaseSurface(); return false; }
            size = value;
            pixels = GC.AllocateUninitializedArray<int>(checked(value.Width * value.Height));
            return true;
        }

        internal void ReleaseSurface()
        {
            if (previousBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero) SelectObject(memoryDc, previousBitmap);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            memoryDc = bitmap = previousBitmap = bits = IntPtr.Zero;
            pixels = [];
            buttonCoverage = [];
            cachedBackground = null;
            pixelsDirty = true;
            paintedPalette = null;
            size = default;
        }

        public void Dispose()
        {
            control.HandleCreated -= HandleCreated;
            control.HandleDestroyed -= HandleDestroyed;
            if (cacheLabel)
            {
                control.Invalidated -= PixelsInvalidated;
                control.TextChanged -= PixelsChanged; control.FontChanged -= PixelsChanged;
                control.BackColorChanged -= PixelsChanged; control.ForeColorChanged -= PixelsChanged;
                control.EnabledChanged -= PixelsChanged;
            }
            ReleaseHandle();
            ReleaseSurface();
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PaintStruct
    {
        public IntPtr Dc; public int Erase; public NativeRect Rect; public int Restore, IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, ImageSize; public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant;
    }
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr window, out PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr window, ref PaintStruct paint);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern int GetClipBox(IntPtr dc, out NativeRect rectangle);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, uint operation);
}
