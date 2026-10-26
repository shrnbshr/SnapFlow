// GPL-3.0-or-later. Native frosted material and a light, non-interactive shadow for editor popups only.
using Microsoft.Win32;
using ShareX.HelpersLib;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ShareX.Roll;

internal sealed class EditorMenuBackdrop : NativeWindow, IDisposable
{
    private readonly ToolStripDropDown menu;
    private readonly CapturePalette palette;
    private readonly bool transparency;
    private static readonly CompositionSetter? setComposition = FindCompositionSetter();
    private readonly ShadowWindow shadow = new();
    private readonly Dictionary<Control, EditorOptionSurface> children = new();
    private IntPtr memoryDc, bitmap, previousBitmap, bits;
    private Size surfaceSize;
    private int[] pixels = [], coverage = [];
    private bool painting, disposed, applied;
    internal bool IsActive { get; private set; }

    internal EditorMenuBackdrop(ToolStripDropDown menu, CapturePalette palette, bool transparency)
    {
        this.menu = menu;
        this.palette = palette;
        this.transparency = transparency;
        menu.HandleCreated += HandleCreated;
        menu.HandleDestroyed += HandleDestroyed;
        menu.Opened += Opened;
        menu.Closed += Closed;
        menu.VisibleChanged += VisibleChanged;
        menu.SizeChanged += GeometryChanged;
        menu.LocationChanged += GeometryChanged;
        menu.Disposed += MenuDisposed;
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
        ObserveChildren(menu);
        if (menu.IsHandleCreated) Attach();
    }

    private uint RemovedMenuMatte()
    {
        uint transmission = palette.Background.GetBrightness() < .5f ? 88u : 112u;
        Color color = palette.Background;
        return transmission << 24 | ((color.R * transmission + 127) / 255) << 16 |
            ((color.G * transmission + 127) / 255) << 8 | (color.B * transmission + 127) / 255;
    }

    private void ObserveChildren(Control parent)
    {
        parent.ControlAdded += ChildAdded;
        parent.ControlRemoved += ChildRemoved;
        foreach (Control child in parent.Controls) ObserveChild(child);
    }
    private void ObserveChild(Control child)
    {
        if (children.ContainsKey(child)) return;
        children.Add(child, new EditorOptionSurface(child, () => IsActive, palette, RemovedMenuMatte()));
        child.Disposed += ChildDisposed;
        ObserveChildren(child);
    }
    private void ChildAdded(object? sender, ControlEventArgs e) { if (e.Control != null) ObserveChild(e.Control); }
    private void ChildRemoved(object? sender, ControlEventArgs e) { if (e.Control != null) ForgetChild(e.Control); }
    private void ChildDisposed(object? sender, EventArgs e) { if (sender is Control control) ForgetChild(control); }
    private void ForgetChild(Control child)
    {
        child.ControlAdded -= ChildAdded;
        child.ControlRemoved -= ChildRemoved;
        child.Disposed -= ChildDisposed;
        foreach (Control nested in child.Controls) ForgetChild(nested);
        if (children.Remove(child, out EditorOptionSurface? surface)) surface.Dispose();
    }

    private void Attach()
    {
        if (Handle != menu.Handle) AssignHandle(menu.Handle);
        applied = false;
        ApplyMaterial();
        UpdateShadow();
    }

    private void HandleCreated(object? sender, EventArgs e) => Attach();
    private void HandleDestroyed(object? sender, EventArgs e)
    {
        ReleaseHandle();
        ReleaseSurface();
        shadow.Release();
        IsActive = applied = false;
    }
    private void Opened(object? sender, EventArgs e)
    {
        ApplyMaterial();
        UpdateShadow();
    }
    private void Closed(object? sender, ToolStripDropDownClosedEventArgs e) => shadow.Release();
    private void VisibleChanged(object? sender, EventArgs e)
    {
        if (menu.Visible) { ApplyMaterial(); UpdateShadow(); }
        else { shadow.Release(); ReleaseSurface(); }
    }
    private void GeometryChanged(object? sender, EventArgs e)
    {
        // EditorOverflow maintains a Region for the opaque fallback before this handler runs.
        // Reassert native corners when its new size belongs to an active frosted popup.
        if (IsActive) ApplyCorners(true);
        UpdateShadow();
    }
    private void MenuDisposed(object? sender, EventArgs e) => Dispose();
    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (disposed || menu.IsDisposed || menu.Disposing || !menu.IsHandleCreated) return;
        try
        {
            menu.BeginInvoke((Action)(() =>
            {
                if (disposed || menu.IsDisposed) return;
                applied = false;
                ApplyMaterial();
                UpdateShadow();
            }));
        }
        catch (InvalidOperationException) { /* The popup closed while the system change was queued. */ }
    }

    private void ApplyMaterial()
    {
        if (disposed || !menu.IsHandleCreated) return;
        bool active = transparency && WindowBackdrop.IsSupported();
        bool changed = active != IsActive || !applied;
        if (changed)
        {
            // A no-activate ToolStrip popup needs explicit background blur. The normal window
            // backdrop reports success here but supplies a solid inactive-window fallback.
            int material = 1;
            MARGINS margins = default;
            bool ready = NativeMethods.DwmSetWindowAttribute(menu.Handle, 38, ref material, sizeof(int)) >= 0;
            ready &= NativeMethods.DwmExtendFrameIntoClientArea(menu.Handle, ref margins) >= 0;
            NativeMethods.UseImmersiveDarkMode(menu.Handle, palette.Background.GetBrightness() < .5f);
            SetRedirectionAlpha(active);
            IsActive = active && ready && SetAccentPolicy(true);
            if (!IsActive) DisableMaterial();
            applied = true;
            menu.Invalidate(true);
        }
        ApplyCorners(IsActive);
    }

    private void SetRedirectionAlpha(bool enabled)
    {
        // Explicit redirection alpha is available on 24H2+. Native blur predates this attribute,
        // so an older Windows version or a failed optional attribute must not block the material.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100) || !menu.IsHandleCreated) return;
        int value = enabled ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(menu.Handle, 39, ref value, sizeof(int));
    }

    private void DisableMaterial()
    {
        SetAccentPolicy(false);
        SetRedirectionAlpha(false);
        int material = 1;
        MARGINS margins = default;
        NativeMethods.DwmSetWindowAttribute(menu.Handle, 38, ref material, sizeof(int));
        NativeMethods.DwmExtendFrameIntoClientArea(menu.Handle, ref margins);
        IsActive = false;
        ReleaseSurface();
    }

    private void ApplyCorners(bool native)
    {
        if (menu.IsDisposed || menu.Width <= 0 || menu.Height <= 0) return;
        int corners = native ? 2 : 1;
        NativeMethods.DwmSetWindowAttribute(menu.Handle, 33, ref corners, sizeof(int));
        int border = SystemInformation.HighContrast ? unchecked((int)0xFFFFFFFF) : unchecked((int)0xFFFFFFFE);
        NativeMethods.DwmSetWindowAttribute(menu.Handle, 34, ref border, sizeof(int));
        Region? previous = menu.Region;
        if (native) menu.Region = null;
        else
        {
            using GraphicsPath outline = CaptureDrawing.Rounded(new RectangleF(0, 0, menu.Width, menu.Height),
                (int)Math.Round(10 * menu.DeviceDpi / 96f));
            menu.Region = new Region(outline);
        }
        previous?.Dispose();
    }

    private static CompositionSetter? FindCompositionSetter()
    {
        IntPtr module = GetModuleHandle("user32.dll");
        IntPtr address = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "SetWindowCompositionAttribute");
        return address == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<CompositionSetter>(address);
    }

    private bool SetAccentPolicy(bool enabled)
    {
        if (setComposition == null || !menu.IsHandleCreated) return !enabled;
        // The compositor supplies blur; the premultiplied surface above supplies a readable theme tint.
        AccentPolicy accent = new() { State = enabled ? 3 : 0 };
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
        try
        {
            Marshal.StructureToPtr(accent, memory, false);
            CompositionData data = new() { Attribute = 19, Data = memory, Size = (nuint)Marshal.SizeOf<AccentPolicy>() };
            return setComposition(menu.Handle, ref data);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private void UpdateShadow()
    {
        if (disposed || !menu.IsHandleCreated || !menu.Visible || SystemInformation.HighContrast)
        {
            shadow.Release();
            return;
        }
        // A failed shadow is decorative only; the original menu stays usable and keeps its native fallback.
        if (!shadow.ShowBehind(menu, palette.Background.GetBrightness() < .5f)) shadow.Release();
    }

    protected override void WndProc(ref Message message)
    {
        if (IsActive && message.Msg == 0x14 && WindowFromDC(message.WParam) != IntPtr.Zero)
        {
            message.Result = new IntPtr(1);
            return;
        }
        if (message.Msg == 0x31E) // WM_DWMCOMPOSITIONCHANGED.
        {
            base.WndProc(ref message);
            applied = false;
            IsActive = false;
            ApplyMaterial();
            UpdateShadow();
            return;
        }
        if (message.Msg is 0x47 or 0x2E0) // WM_WINDOWPOSCHANGED / WM_DPICHANGED.
        {
            base.WndProc(ref message);
            UpdateShadow();
            return;
        }
        if (!IsActive || painting || message.Msg != 0xF || message.WParam != IntPtr.Zero
            || menu.ClientSize.Width <= 0 || menu.ClientSize.Height <= 0
            || menu.Renderer is not EditorToolStripRenderer renderer)
        {
            base.WndProc(ref message);
            return;
        }
        if (!EnsureSurface(menu.ClientSize))
        {
            // Restore opaque rendering rather than leaving an unpainted transparent popup.
            DisableMaterial();
            ApplyCorners(false);
            base.WndProc(ref message);
            return;
        }
        IntPtr dc = BeginPaint(message.HWnd, out PaintStruct paint);
        painting = true;
        try
        {
            if (GetClipBox(dc, out _) != 1)
            {
                // Render normal ink and the matching material coverage separately. Text, icons and hosted
                // fields retain their native drawing and input; only the popup's background reveals DWM.
                SendMessage(message.HWnd, 0x318, memoryDc, new IntPtr(4 | 8));
                GdiFlush();
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                bool previousCoverage = renderer.PaintingCoverage;
                renderer.PaintingCoverage = true;
                try { SendMessage(message.HWnd, 0x318, memoryDc, new IntPtr(4 | 8)); }
                finally { renderer.PaintingCoverage = previousCoverage; }
                GdiFlush();
                Marshal.Copy(bits, coverage, 0, coverage.Length);
                BackdropSnapshot.RemoveButtonMatte(MemoryMarshal.Cast<int, uint>(pixels.AsSpan()), coverage,
                    RemovedMenuMatte());
                Marshal.Copy(pixels, 0, bits, pixels.Length);
                BitBlt(dc, 0, 0, surfaceSize.Width, surfaceSize.Height, memoryDc, 0, 0, 0x00CC0020);
            }
        }
        finally
        {
            painting = false;
            EndPaint(message.HWnd, ref paint);
        }
        message.Result = IntPtr.Zero;
    }

    private bool EnsureSurface(Size size)
    {
        if (surfaceSize == size && memoryDc != IntPtr.Zero) return true;
        ReleaseSurface();
        BitmapInfo info = new() { Size = 40, Width = size.Width, Height = -size.Height, Planes = 1, BitCount = 32 };
        memoryDc = CreateCompatibleDC(IntPtr.Zero);
        if (memoryDc != IntPtr.Zero) bitmap = CreateDIBSection(memoryDc, ref info, 0, out bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) { ReleaseSurface(); return false; }
        previousBitmap = SelectObject(memoryDc, bitmap);
        if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1))
        {
            previousBitmap = IntPtr.Zero;
            ReleaseSurface();
            return false;
        }
        surfaceSize = size;
        pixels = GC.AllocateUninitializedArray<int>(checked(size.Width * size.Height));
        coverage = GC.AllocateUninitializedArray<int>(pixels.Length);
        return true;
    }

    private void ReleaseSurface()
    {
        if (previousBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero) SelectObject(memoryDc, previousBitmap);
        if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
        if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
        memoryDc = bitmap = previousBitmap = bits = IntPtr.Zero;
        surfaceSize = default;
        pixels = coverage = [];
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        menu.ControlAdded -= ChildAdded;
        menu.ControlRemoved -= ChildRemoved;
        foreach (Control child in children.Keys.ToArray()) ForgetChild(child);
        SystemEvents.UserPreferenceChanged -= PreferenceChanged;
        menu.HandleCreated -= HandleCreated;
        menu.HandleDestroyed -= HandleDestroyed;
        menu.Opened -= Opened;
        menu.Closed -= Closed;
        menu.VisibleChanged -= VisibleChanged;
        menu.SizeChanged -= GeometryChanged;
        menu.LocationChanged -= GeometryChanged;
        menu.Disposed -= MenuDisposed;
        if (menu.IsHandleCreated && !menu.IsDisposed && !menu.Disposing)
        {
            DisableMaterial();
            ApplyCorners(false);
            menu.Invalidate(true);
        }
        IsActive = false;
        ReleaseHandle();
        ReleaseSurface();
        shadow.Release();
    }

    private sealed class ShadowWindow : NativeWindow
    {
        private IntPtr memoryDc, bitmap, previousBitmap, bits;
        private Size menuSize, textureSize;
        private float scale;
        private bool dark;
        private int gutter;

        internal bool ShowBehind(ToolStripDropDown menu, bool isDark)
        {
            if (!GetWindowRect(menu.Handle, out NativeRect bounds)) return false;
            Size size = new(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            if (size.Width <= 0 || size.Height <= 0)
                return false;
            float dpi = menu.DeviceDpi / 96f;
            try
            {
                if (!EnsureTexture(size, dpi, isDark)) return false;
                if (Handle == IntPtr.Zero)
                {
                    CreateHandle(new CreateParams
                    {
                        Caption = "SnapFlow.EditorMenuShadow", Style = unchecked((int)0x80000000),
                        ExStyle = 0x080800A0 // NOACTIVATE | LAYERED | TRANSPARENT | TOOLWINDOW.
                    });
                }
                NativePoint destination = new() { X = bounds.Left - gutter, Y = bounds.Top - gutter };
                NativePoint origin = default;
                NativeSize dimensions = new() { Width = textureSize.Width, Height = textureSize.Height };
                BlendFunction blend = new() { Alpha = 255, Format = 1 };
                if (!UpdateLayeredWindow(Handle, IntPtr.Zero, ref destination, ref dimensions, memoryDc,
                    ref origin, 0, ref blend, 2)) return false;
                // Inserting after the popup keeps this window immediately behind it without activation.
                if (!SetWindowPos(Handle, menu.Handle, destination.X, destination.Y,
                    textureSize.Width, textureSize.Height, 0x650)) // NOACTIVATE | SHOWWINDOW | NOOWNERZORDER | NOSENDCHANGING.
                    return false;
                return IsWindowVisible(Handle);
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        private bool EnsureTexture(Size size, float dpi, bool isDark)
        {
            if (memoryDc != IntPtr.Zero && menuSize == size && scale == dpi && dark == isDark) return true;
            ReleaseTexture();
            gutter = (int)Math.Ceiling(24 * dpi);
            textureSize = new(size.Width + 2 * gutter, size.Height + 2 * gutter);
            RectangleF face = new(gutter, gutter, size.Width, size.Height);
            using Bitmap ambient = ButtonElevation.CreateShadow(textureSize, dpi, 5, 2, face, 10 * dpi, 5, false);
            using Bitmap contact = ButtonElevation.CreateShadow(textureSize, dpi, 2, 1, face, 10 * dpi, 2, false);
            using Bitmap texture = new(textureSize.Width, textureSize.Height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(texture))
            {
                graphics.Clear(Color.Transparent);
                void Layer(Bitmap source, float opacity)
                {
                    using ImageAttributes attributes = new();
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = opacity });
                    graphics.DrawImage(source, new Rectangle(Point.Empty, textureSize), 0, 0,
                        textureSize.Width, textureSize.Height, GraphicsUnit.Pixel, attributes);
                }
                Layer(ambient, isDark ? .16f : .07f);
                Layer(contact, isDark ? .05f : .025f);
                // Do not put a dark rectangle behind the translucent material. Keep only the halo outside
                // the exact rounded popup shape, including its corners.
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using GraphicsPath hole = CaptureDrawing.Rounded(face, 10 * dpi);
                using SolidBrush clear = new(Color.Transparent);
                graphics.FillPath(clear, hole);
            }
            BitmapInfo info = new()
            {
                Size = 40, Width = textureSize.Width, Height = -textureSize.Height, Planes = 1, BitCount = 32
            };
            memoryDc = CreateCompatibleDC(IntPtr.Zero);
            if (memoryDc != IntPtr.Zero) bitmap = CreateDIBSection(memoryDc, ref info, 0, out bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) { ReleaseTexture(); return false; }
            previousBitmap = SelectObject(memoryDc, bitmap);
            if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1))
            {
                previousBitmap = IntPtr.Zero;
                ReleaseTexture();
                return false;
            }
            BitmapData data = texture.LockBits(new Rectangle(Point.Empty, textureSize), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int[] row = new int[textureSize.Width];
                for (int y = 0; y < textureSize.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    Marshal.Copy(row, 0, IntPtr.Add(bits, y * textureSize.Width * 4), row.Length);
                }
            }
            finally { texture.UnlockBits(data); }
            menuSize = size;
            scale = dpi;
            dark = isDark;
            return true;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) { message.Result = new IntPtr(-1); return; } // HTTRANSPARENT.
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE.
            base.WndProc(ref message);
        }

        private void ReleaseTexture()
        {
            if (previousBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero) SelectObject(memoryDc, previousBitmap);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            memoryDc = bitmap = previousBitmap = bits = IntPtr.Zero;
            menuSize = textureSize = default;
        }

        internal void Release()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
            ReleaseTexture();
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct AccentPolicy { public int State; public uint Flags, Gradient, Animation; }
    [StructLayout(LayoutKind.Sequential)] private struct CompositionData { public int Attribute; public IntPtr Data; public nuint Size; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool CompositionSetter(IntPtr window, ref CompositionData data);
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BlendFunction { public byte Operation, Flags, Alpha, Format; }
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
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string module);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc, ref NativePoint destination,
        ref NativeSize size, IntPtr sourceDc, ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern int GetClipBox(IntPtr dc, out NativeRect rectangle);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
}
