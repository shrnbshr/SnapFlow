// GPL-3.0-or-later. Apply the visible button face to the native window's hit region.
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace ShareX.Roll;

internal sealed class ButtonPointerBounds
{
    private enum BoundsKind { None, Face, CompactFace, Inset, Toggle }

    private static readonly ConditionalWeakTable<Control, ButtonPointerBounds> windows = new();
    private readonly Control control;
    private BoundsKind kind;
    private float? radius;
    private Region? applied;

    private ButtonPointerBounds(Control control)
    {
        this.control = control;
        control.SizeChanged += (_, _) => Update();
        control.DpiChangedAfterParent += (_, _) => Update();
        control.Disposed += (_, _) => applied?.Dispose();
    }

    internal static void Apply(Control control, bool enabled, bool compact = false, float? radius = null) =>
        Apply(control, enabled ? (compact ? BoundsKind.CompactFace : BoundsKind.Face) : BoundsKind.None, radius);

    internal static void ApplyInset(Control control, bool enabled, float radius) =>
        Apply(control, enabled ? BoundsKind.Inset : BoundsKind.None, radius);

    internal static void ApplyToggle(MotionToggle toggle, CapturePalette palette) =>
        Apply(toggle, palette.HasShapedControls ? BoundsKind.Toggle : BoundsKind.None, palette.CornerRadius(12));

    private static void Apply(Control control, BoundsKind kind, float? radius)
    {
        if (kind == BoundsKind.None && !windows.TryGetValue(control, out _)) return;
        ButtonPointerBounds bounds = windows.GetValue(control, button => new(button));
        if (bounds.kind == kind && bounds.radius == radius) return;
        bounds.kind = kind;
        bounds.radius = radius;
        bounds.Update();
    }

    private void Update()
    {
        Region? next = null;
        Size size = control.ClientSize;
        if (kind != BoundsKind.None && size.Width > 0 && size.Height > 0)
        {
            float scale = control.DeviceDpi / 96f;
            RectangleF face = kind switch
            {
                BoundsKind.CompactFace => ButtonElevation.CompactFaceBounds(size, scale),
                BoundsKind.Inset => new RectangleF(scale, scale, size.Width - 2 * scale, size.Height - 2 * scale),
                BoundsKind.Toggle => MotionToggle.TrackBounds(size, scale),
                _ => ButtonElevation.FaceBounds(size, scale)
            };
            float corner = radius is float specified ? specified * scale : kind == BoundsKind.CompactFace
                ? ButtonElevation.CompactRadius(face, scale) : ButtonElevation.Radius(face, scale);
            using GraphicsPath path = CaptureDrawing.Rounded(face, corner);
            next = new Region(path);
        }
        if (next == null && applied == null) return;
        Region? previous = applied;
        control.Region = applied = next;
        previous?.Dispose();
    }
}
