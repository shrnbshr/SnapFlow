// GPL-3.0-or-later. Editor hints stay readable independently of the window material.
using System.Drawing.Text;

namespace ShareX.Roll;

internal static class EditorToolTips
{
    private const TextFormatFlags TextFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    internal static void Install(Form owner, CapturePalette palette, params ToolStrip[] strips)
    {
        ToolTip tip = new() { OwnerDraw = true, UseAnimation = false, UseFading = false };
        ToolStrip? anchor = null;
        ToolStripItem? current = null;
        owner.Disposed += (_, _) => tip.Dispose();
        tip.Popup += (_, e) =>
        {
            if (anchor == null || current == null) { e.Cancel = true; return; }
            int scale = anchor.DeviceDpi;
            using Graphics graphics = anchor.CreateGraphics();
            Size text = TextRenderer.MeasureText(graphics, current.ToolTipText, anchor.Font, Size.Empty, TextFlags);
            e.ToolTipSize = new(text.Width + (int)Math.Round(16 * scale / 96f),
                text.Height + (int)Math.Round(10 * scale / 96f));
        };
        tip.Draw += (_, e) =>
        {
            if (anchor == null) return;
            CapturePalette colors = SystemInformation.HighContrast ? CapturePalette.Current : palette;
            e.Graphics.Clear(colors.Surface);
            CaptureDrawing.DrawInsideBorder(e.Graphics, e.Bounds, 0, colors.Border, anchor.DeviceDpi / 96f);
            e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, anchor.Font, e.Bounds, colors.Text,
                TextFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | UiLanguage.TextDirection);
        };

        void Hide()
        {
            if (anchor is { IsDisposed: false }) tip.Hide(anchor);
            anchor = null;
            current = null;
        }

        owner.Deactivate += (_, _) => Hide();
        foreach (ToolStrip strip in strips)
        {
            strip.ShowItemToolTips = false;
            strip.MouseLeave += (_, _) => Hide();
            strip.VisibleChanged += (_, _) => { if (!strip.Visible && anchor == strip) Hide(); };
            foreach (ToolStripItem item in strip.Items.Cast<ToolStripItem>().Append(strip.OverflowButton))
            {
                item.MouseHover += (_, _) =>
                {
                    if (!item.Enabled || item.GetCurrentParent() != strip || string.IsNullOrEmpty(item.ToolTipText) ||
                        item is ToolStripDropDownItem { Pressed: true }) return;
                    Hide();
                    anchor = strip;
                    current = item;
                    tip.Show(item.ToolTipText, strip, new Point(item.Bounds.Left,
                        strip.Height + (int)Math.Round(3 * strip.DeviceDpi / 96f)), tip.AutoPopDelay);
                };
                item.MouseLeave += (_, _) => { if (current == item) Hide(); };
                item.MouseDown += (_, _) => Hide();
            }
        }
    }
}
