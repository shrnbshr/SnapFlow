// GPL-3.0-or-later. Match the shared shadow material to the editor's existing 4px halo.
using System.Drawing.Imaging;

namespace ShareX.Roll;

internal sealed class EditorButtonShadow : IDisposable
{
    private Bitmap? ambient, contact;
    private RectangleF cachedFace;
    private float cachedScale, cachedRadius;

    internal void Paint(Graphics graphics, Size size, RectangleF face, float radius, float scale, bool dark, float amount)
    {
        if (amount <= 0 || SystemInformation.HighContrast || size.Width <= 0 || size.Height <= 0) return;
        if (ambient?.Size != size || cachedFace != face || cachedScale != scale || cachedRadius != radius)
        {
            Dispose();
            // The same two directional layers as the workspace/settings, fitted inside the editor's
            // narrower gutter. Keep the visible face, icon positions and native hit target unchanged.
            float blurScale = scale * 2 / 3;
            ambient = ButtonElevation.CreateShadow(size, blurScale, 2.3f, 1.5f, face, radius, 1.25f, true);
            contact = ButtonElevation.CreateShadow(size, blurScale, 1.2f, 1, face, radius, .7f, true);
            cachedFace = face; cachedScale = scale; cachedRadius = radius;
        }
        void Layer(Bitmap texture, float strength)
        {
            using ImageAttributes opacity = new();
            opacity.SetColorMatrix(new ColorMatrix { Matrix33 = amount * strength });
            graphics.DrawImage(texture, new Rectangle(Point.Empty, size), 0, 0, size.Width, size.Height, GraphicsUnit.Pixel, opacity);
        }
        Layer(ambient!, dark ? .48f : .18f);
        Layer(contact!, dark ? .14f : .05f);
    }

    public void Dispose()
    {
        ambient?.Dispose(); contact?.Dispose();
        ambient = contact = null;
    }
}
