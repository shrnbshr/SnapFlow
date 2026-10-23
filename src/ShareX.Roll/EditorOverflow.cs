// GPL-3.0-or-later. Keep WinForms overflow commands and keyboard handling; change presentation only.
namespace ShareX.Roll;

internal static class EditorOverflow
{
    private sealed record ItemStyle(ToolStripItemDisplayStyle Display, Size Size, Padding Padding,
        Padding Margin, ContentAlignment ImageAlign, ContentAlignment TextAlign, TextImageRelation Relation,
        string? Text, ToolStripDropDownDirection Direction);

    private static int Group(ToolStripItem item) => item.Tag is ShareX.ScreenCaptureLib.ShapeType type ? type switch
    {
        ShareX.ScreenCaptureLib.ShapeType.DrawingSmartEraser or ShareX.ScreenCaptureLib.ShapeType.EffectBlur or
        ShareX.ScreenCaptureLib.ShapeType.EffectPixelate or ShareX.ScreenCaptureLib.ShapeType.EffectHighlight or
        ShareX.ScreenCaptureLib.ShapeType.ToolSpotlight => 1,
        _ => 0
    } : 2;

    internal static void Install(ToolStrip strip, string label)
    {
        int S(int value) => (int)Math.Round(value * strip.DeviceDpi / 96f);
        ToolStripOverflowButton button = strip.OverflowButton;
        button.AutoSize = false;
        button.Padding = new Padding(S(10), 0, S(10), 0);
        button.Size = new(S(36), S(36));
        button.ToolTipText = button.AccessibleName = label;
        ToolStripDropDown menu = button.DropDown;
        menu.AccessibleName = label;
        menu.Font = strip.Font;
        menu.Renderer = strip.Renderer;
        menu.Padding = new Padding(S(6));
        menu.DropShadowEnabled = true;
        // The default overflow uses a wrapping flow layout, producing an unlabeled icon grid.
        FlowLayoutSettings flow = (FlowLayoutSettings)menu.LayoutSettings!;
        flow.FlowDirection = FlowDirection.TopDown;
        flow.WrapContents = false;
        Dictionary<ToolStripItem, ItemStyle> original = new();
        void Restore()
        {
            if (original.Count == 0) return;
            foreach ((ToolStripItem item, ItemStyle style) in original)
            {
                if (item.IsDisposed) continue;
                item.DisplayStyle = style.Display;
                item.Size = style.Size;
                item.Padding = style.Padding;
                item.Margin = style.Margin;
                item.ImageAlign = style.ImageAlign;
                item.TextAlign = style.TextAlign;
                item.TextImageRelation = style.Relation;
                item.Text = style.Text;
                if (item is ToolStripDropDownItem dropdown) dropdown.DropDownDirection = style.Direction;
            }
            original.Clear();
            if (!strip.IsDisposed) strip.ResumeLayout(true);
        }
        menu.Opening += (_, _) =>
        {
            Restore();
            ToolStripItem[] items = strip.Items.Cast<ToolStripItem>()
                .Where(item => item.Available && item.Placement == ToolStripItemPlacement.Overflow).ToArray();
            if (items.Length == 0) return;
            // Wider menu labels must not change which buttons fit in the parent toolbar.
            strip.SuspendLayout();
            int width = Math.Max(S(208), items.Max(item => TextRenderer.MeasureText(item.Text, menu.Font).Width) + S(68));
            width = Math.Min(width, Screen.FromControl(strip).WorkingArea.Width - S(24));
            int height = Math.Max(S(32), menu.Font.Height + S(12));
            int previousGroup = -1;
            foreach (ToolStripItem item in items)
            {
                original[item] = new(item.DisplayStyle, item.Size, item.Padding, item.Margin,
                    item.ImageAlign, item.TextAlign, item.TextImageRelation, item.Text,
                    item is ToolStripDropDownItem dropdown ? dropdown.DropDownDirection : ToolStripDropDownDirection.Default);
                item.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
                item.Size = new(width, height);
                item.Padding = new Padding(S(8), 0, S(16), 0);
                int group = Group(item);
                item.Margin = new Padding(0, S(previousGroup >= 0 && previousGroup != group ? 7 : 1), 0, S(1));
                previousGroup = group;
                item.ImageAlign = item.TextAlign = ContentAlignment.MiddleLeft;
                item.TextImageRelation = TextImageRelation.ImageBeforeText;
                if (item.Tag is ShareX.ScreenCaptureLib.ShapeType)
                {
                    string text = item.Text ?? string.Empty;
                    int prefix = text.IndexOfAny([':', '：']);
                    if (prefix >= 0) item.Text = text[(prefix + 1)..].TrimStart();
                }
                if (item is ToolStripDropDownItem submenu) submenu.DropDownDirection = ToolStripDropDownDirection.Right;
            }
            menu.ImageScalingSize = strip.ImageScalingSize;
            menu.PerformLayout();
        };
        menu.Closed += (_, _) => Restore();
        menu.Disposed += (_, _) => Restore();
        menu.SizeChanged += (_, _) =>
        {
            using var outline = CaptureDrawing.Rounded(new RectangleF(0, 0, menu.Width, menu.Height), S(10));
            Region? previous = menu.Region;
            menu.Region = new Region(outline);
            previous?.Dispose();
        };
        strip.SizeChanged += (_, _) => { if (menu.Visible) menu.Close(); };
    }
}
