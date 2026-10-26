// GPL-3.0-or-later. Soft button materials with reserved space outside the visible face.
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ShareX.Roll;

internal static class ButtonSpacing
{
    internal const int Horizontal = 12;
    internal const int Leading = 16;
    internal const int Vertical = 8;
    internal const int IconGap = 8;
    internal const int ActionHeight = 64;
    internal const int ToolHeight = 52;
    internal const int FieldHeight = 40;
    // The painter's content minimum includes the shadow halo; native AutoSize must not add it again.
    internal static Padding ActionPadding => new(Horizontal, Vertical, Horizontal, Vertical);
}

internal sealed class ButtonElevation : IDisposable
{
    internal const int ResponseMilliseconds = 160;
    internal const float Lift = 1f;
    private Bitmap? ambient, contact;
    private ImageAttributes? opacity;
    private readonly ColorMatrix opacityMatrix = new();
    private float shadowScale;
    private RectangleF shadowFace;
    private float shadowRadius;
    private bool shadowUnified;

    internal static Color Surface(CapturePalette palette) => SystemInformation.HighContrast ? palette.Surface :
        palette.Background.GetBrightness() < .5f ? CaptureDrawing.Blend(palette.Surface, palette.Hover, .4f) : palette.Canvas;

    internal static RectangleF FaceBounds(Size size, float scale)
    {
        float x = Math.Min(16 * scale, size.Width / 6f);
        float y = Math.Min(12 * scale, Math.Max(0, (size.Height - 24 * scale) / 2f));
        return new RectangleF(x, y, Math.Max(1, size.Width - 2 * x), Math.Max(1, size.Height - 2 * y));
    }

    internal static float Radius(RectangleF face, float scale) => Math.Min(20 * scale, face.Height * .34f);
    internal static float CompactRadius(RectangleF face, float scale) => Math.Min(16 * scale, Radius(face, scale));

    // Toolbars and settings rows reserve a smaller halo without shrinking their native hit targets.
    internal static RectangleF CompactFaceBounds(Size size, float scale)
    {
        float x = Math.Min(8 * scale, size.Width / 6f);
        float y = Math.Min(6 * scale, Math.Max(0, (size.Height - 24 * scale) / 2f));
        return new RectangleF(x, y, Math.Max(1, size.Width - 2 * x), Math.Max(1, size.Height - 2 * y));
    }

    internal void PaintShadow(Graphics graphics, Size size, float scale, bool dark, float amount, bool compact = false, bool unified = false)
    {
        if (amount <= 0 || SystemInformation.HighContrast || size.Width <= 0 || size.Height <= 0) return;
        RectangleF face = compact ? CompactFaceBounds(size, scale) : FaceBounds(size, scale);
        float cornerRadius = compact ? CompactRadius(face, scale) : Radius(face, scale);
        // Non-editor controls share one logical blur scale, including compact cards.
        // Keep the original recipe as the default for the editor's existing renderer.
        if (compact && !unified) scale *= .5f;
        if (ambient?.Size != size || shadowScale != scale || shadowFace != face || shadowRadius != cornerRadius || shadowUnified != unified)
        {
            Dispose();
            // Three blur passes need three kernel radii of empty space. The common
            // profile fits the smallest 8px / 6px gutter without trimming its tail.
            ambient = CreateShadow(size, scale, unified ? 2.3f : 5, unified ? 1.5f : 4,
                face, cornerRadius, unified ? 1.25f : 5, unified);
            contact = CreateShadow(size, scale, unified ? 1.2f : 2, unified ? 1 : 1.5f,
                face, cornerRadius, unified ? .7f : 2, unified);
            shadowScale = scale;
            shadowFace = face;
            shadowRadius = cornerRadius;
            shadowUnified = unified;
        }
        void Layer(Bitmap texture, float strength)
        {
            opacity ??= new ImageAttributes();
            opacityMatrix.Matrix33 = amount * strength;
            opacity.SetColorMatrix(opacityMatrix);
            graphics.DrawImage(texture, new Rectangle(Point.Empty, size), 0, 0, size.Width, size.Height, GraphicsUnit.Pixel, opacity);
        }
        Layer(ambient!, dark ? .48f : .14f);
        Layer(contact!, dark ? .14f : .045f);
    }

    internal static Bitmap CreateShadow(Size size, float scale, float softness, float offset, RectangleF face, float cornerRadius, float verticalSoftness, bool directional)
    {
        using Bitmap mask = new(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(mask))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            face.Offset(0, offset * scale);
            using GraphicsPath shape = CaptureDrawing.Rounded(face, cornerRadius);
            using SolidBrush fill = new(Color.White);
            graphics.FillPath(fill, shape);
        }
        int[] pixels = new int[checked(size.Width * size.Height)];
        BitmapData input = mask.LockBits(new Rectangle(Point.Empty, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < size.Height; y++)
                Marshal.Copy(IntPtr.Add(input.Scan0, y * input.Stride), pixels, y * size.Width, size.Width);
        }
        finally { mask.UnlockBits(input); }

        float[] alpha = pixels.Select(pixel => (float)((uint)pixel >> 24)).ToArray();
        float[] temporary = new float[pixels.Length];
        int radiusX = Math.Max(1, (int)Math.Round(softness * scale - .5f));
        int radiusY = Math.Max(1, (int)Math.Round(verticalSoftness * scale - .5f));
        // Three sliding box convolutions approximate a Gaussian in O(pixels), independent of blur radius.
        // The texture is prepared only when size/DPI changes; animation only changes layer opacity.
        for (int pass = 0; pass < 3; pass++)
        {
            Blur(alpha, temporary, size.Width, size.Height, radiusX, horizontal: true);
            Blur(temporary, alpha, size.Width, size.Height, radiusY, horizontal: false);
        }
        for (int y = 0; y < size.Height; y++)
        for (int x = 0; x < size.Width; x++)
        {
            int i = y * size.Width + x;
            float opacity = alpha[i];
            if (directional)
            {
                // Light from above leaves the top edge clear. Shape this falloff from
                // the face itself, never from the rectangular bitmap boundary.
                float lower = Math.Clamp((y - face.Top) / (6 * scale), 0, 1);
                opacity *= lower * lower * (3 - 2 * lower);
            }
            pixels[i] = (int)Math.Clamp(MathF.Round(opacity), 0, 255) << 24;
        }
        Bitmap result = new(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        BitmapData output = result.LockBits(new Rectangle(Point.Empty, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (int y = 0; y < size.Height; y++)
                Marshal.Copy(pixels, y * size.Width, IntPtr.Add(output.Scan0, y * output.Stride), size.Width);
        }
        finally { result.UnlockBits(output); }
        return result;
    }

    private static void Blur(float[] input, float[] output, int width, int height, int radius, bool horizontal)
    {
        int rows = horizontal ? height : width, columns = horizontal ? width : height;
        int stride = horizontal ? 1 : width;
        for (int row = 0; row < rows; row++)
        {
            int start = horizontal ? row * width : row;
            float sum = 0;
            for (int i = 0; i <= Math.Min(radius, columns - 1); i++) sum += input[start + i * stride];
            for (int column = 0; column < columns; column++)
            {
                output[start + column * stride] = sum / (2 * radius + 1);
                if (column - radius >= 0) sum -= input[start + (column - radius) * stride];
                if (column + radius + 1 < columns) sum += input[start + (column + radius + 1) * stride];
            }
        }
    }

    public void Dispose()
    {
        ambient?.Dispose(); contact?.Dispose();
        opacity?.Dispose(); opacity = null;
        ambient = contact = null;
    }
}
