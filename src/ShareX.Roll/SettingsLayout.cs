// GPL-3.0-or-later. Shared geometry for settings rows; values are logical pixels.
namespace ShareX.Roll;

internal sealed class SettingsOptionHeader : TableLayoutPanel
{
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Parent is SettingsGroup group) group.PaintChildBackground(this, e.Graphics);
        else base.OnPaintBackground(e);
    }
}

internal static class SettingsLayout
{
    internal const int Inset = 16;
    internal const int Vertical = 10;
    internal const int RowHeight = 64;
    internal const int Gap = 12;
    internal const int SectionGap = 20;
    internal const int CaptionGap = 2;

    internal static void SetBottomGap(Control control, int gap = Gap)
    {
        // A nested UserControl scales before its parent assigns Margin. Reapply the
        // logical gap at its final DPI so it matches ordinary controls in the stack.
        void Apply() => control.Margin = new Padding(0, 0, 0, (int)Math.Round(gap * control.DeviceDpi / 96f));
        control.Margin = new Padding(0, 0, 0, gap);
        control.HandleCreated += (_, _) => Apply();
        control.DpiChangedAfterParent += (_, _) => Apply();
        if (control.IsHandleCreated) Apply();
    }

    internal static FlowLayoutPanel ConfigureOption(TableLayoutPanel row, Label title, Label hint, Control accessory)
    {
        row.AutoSize = true;
        row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        row.MinimumSize = new Size(0, RowHeight);
        row.ColumnCount = 2;
        row.RowCount = 1;
        row.Margin = Padding.Empty;
        row.Padding = new Padding(Inset, Vertical, Inset, Vertical);
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        FlowLayoutPanel caption = new()
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = Padding.Empty
        };
        title.AutoSize = hint.AutoSize = true;
        title.Margin = Padding.Empty;
        hint.Margin = new Padding(0, CaptionGap, 0, 0);
        caption.Controls.AddRange([title, hint]);
        accessory.Anchor = AnchorStyles.Right;
        accessory.Margin = new Padding(Gap, 0, 0, 0);
        void Wrap()
        {
            int width = Math.Max(1, row.ClientSize.Width - row.Padding.Horizontal - accessory.Width - accessory.Margin.Horizontal);
            foreach (Label label in caption.Controls)
                label.MaximumSize = new Size(width, 0);
        }
        row.SizeChanged += (_, _) => Wrap();
        accessory.SizeChanged += (_, _) => Wrap();
        row.Controls.Add(caption, 0, 0);
        row.Controls.Add(accessory, 1, 0);
        return caption;
    }

    internal static void MatchOptionHeights(Control owner, params TableLayoutPanel[] rows)
    {
        bool queued = false;
        void Update()
        {
            if (queued || !owner.IsHandleCreated || owner.IsDisposed) return;
            queued = true;
            owner.BeginInvoke((Action)(() =>
            {
                queued = false;
                if (owner.IsDisposed) return;
                int height = (int)Math.Round(RowHeight * owner.DeviceDpi / 96f);
                foreach (TableLayoutPanel row in rows)
                {
                    FlowLayoutPanel caption = row.Controls.OfType<FlowLayoutPanel>().Single();
                    Control accessory = row.Controls.Cast<Control>().Single(control => control != caption);
                    height = Math.Max(height, Math.Max(caption.PreferredSize.Height, accessory.Height) + row.Padding.Vertical);
                }
                foreach (TableLayoutPanel row in rows)
                    if (row.MinimumSize.Height != height) row.MinimumSize = new Size(0, height);
            }));
        }
        owner.HandleCreated += (_, _) => Update();
        owner.DpiChangedAfterParent += (_, _) => Update();
        foreach (TableLayoutPanel row in rows)
        {
            row.SizeChanged += (_, _) => Update();
            row.Controls.OfType<FlowLayoutPanel>().Single().SizeChanged += (_, _) => Update();
        }
        Update();
    }
}
