// GPL-3.0-or-later. Preserve opaque input/text pixels while hosted option rows share menu glass.
using System.Runtime.InteropServices;

namespace ShareX.Roll;

internal sealed class EditorOptionSurface : NativeWindow, IDisposable
{
    private readonly Control control;
    private readonly Func<bool> active;
    private readonly CapturePalette palette;
    private readonly uint matte;
    private IntPtr memoryDc, bitmap, previousBitmap, bits;
    private Size size;
    private int[] pixels = [];
    private bool painting;

    internal EditorOptionSurface(Control control, Func<bool> active, CapturePalette palette, uint matte)
    {
        this.control = control;
        this.active = active;
        this.palette = palette;
        this.matte = matte;
        control.HandleCreated += HandleCreated;
        control.HandleDestroyed += HandleDestroyed;
        if (control.IsHandleCreated) AssignHandle(control.Handle);
    }
    private void HandleCreated(object? sender, EventArgs e) => AssignHandle(control.Handle);
    private void HandleDestroyed(object? sender, EventArgs e) { ReleaseHandle(); ReleaseSurface(); }

    protected override void WndProc(ref Message message)
    {
        if (active() && message.Msg == 0x14 && WindowFromDC(message.WParam) != IntPtr.Zero)
        {
            message.Result = new IntPtr(1);
            return;
        }
        if (!active() || painting || message.Msg != 0xF || message.WParam != IntPtr.Zero ||
            control.Width <= 0 || control.Height <= 0 || !EnsureSurface(control.ClientSize))
        {
            base.WndProc(ref message);
            return;
        }
        IntPtr dc = BeginPaint(message.HWnd, out PaintStruct paint);
        painting = true;
        try
        {
            // Native edit windows can reuse the target DC's text colors during WM_PRINTCLIENT.
            // A fresh memory DC defaults to black ink, which disappears on dark numeric fields.
            Color fore = control.ForeColor, back = control.BackColor;
            SetTextColor(memoryDc, fore.R | fore.G << 8 | fore.B << 16);
            SetBkColor(memoryDc, back.R | back.G << 8 | back.B << 16);
            SendMessage(message.HWnd, 0x318, memoryDc, new IntPtr(4 | 8));
            GdiFlush();
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            uint background = (uint)palette.Background.ToArgb() & 0xFFFFFF;
            uint tint = BackdropSnapshot.RemoveMatte(background, matte);
            for (int index = 0; index < pixels.Length; index++)
            {
                uint pixel = (uint)pixels[index];
                pixels[index] = (int)((pixel & 0xFFFFFF) == background ? tint : pixel | 0xFF000000);
            }
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            BitBlt(dc, 0, 0, size.Width, size.Height, memoryDc, 0, 0, 0x00CC0020);
        }
        finally { painting = false; EndPaint(message.HWnd, ref paint); }
        message.Result = IntPtr.Zero;
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
        if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1))
        {
            previousBitmap = IntPtr.Zero;
            ReleaseSurface();
            return false;
        }
        size = value;
        pixels = new int[checked(value.Width * value.Height)];
        return true;
    }

    private void ReleaseSurface()
    {
        if (previousBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero) SelectObject(memoryDc, previousBitmap);
        if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
        if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
        memoryDc = bitmap = previousBitmap = bits = IntPtr.Zero;
        pixels = [];
        size = default;
    }
    public void Dispose()
    {
        control.HandleCreated -= HandleCreated;
        control.HandleDestroyed -= HandleDestroyed;
        ReleaseHandle();
        ReleaseSurface();
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
    [DllImport("gdi32.dll")] private static extern int SetTextColor(IntPtr dc, int color);
    [DllImport("gdi32.dll")] private static extern int SetBkColor(IntPtr dc, int color);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
}
