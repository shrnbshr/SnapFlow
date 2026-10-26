// GPL-3.0-or-later. Clip native child windows without remeasuring their controls per frame.
using System.Diagnostics;

namespace ShareX.Roll;

internal sealed class SettingsReveal : Panel
{
    private readonly ScrollingOptionsEditor editor;
    private readonly UiTween reveal;
    private int fullHeight, arrangedWidth;
    private bool expanded, arranging, stackDirty;
    internal bool IsAnimating => reveal.IsRunning;
    internal float CurrentHeight => Height;
    internal long UpdateTicks { get; private set; }
    internal long LayoutTicks { get; private set; }
    internal long PresentTicks { get; private set; }
    internal int UpdatedFrames { get; private set; }
    internal Action? PresentFrame { get; set; }

    internal SettingsReveal(ScrollingOptionsEditor editor)
    {
        Name = "scrollingReveal";
        AutoSize = false;
        Dock = DockStyle.Top;
        Height = 0;
        Margin = Padding.Empty;
        TabStop = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        this.editor = editor;
        reveal = new UiTween(this, ApplyHeight, useFrameClock: true);
        reveal.Completed += () => { if (!expanded) editor.Hide(); };
        editor.Dock = DockStyle.Top;
        Controls.Add(editor);
        editor.SizeChanged += (_, _) =>
        {
            if (arranging) return;
            fullHeight = editor.Height;
            if (!reveal.IsRunning) ApplyHeight();
        };
        editor.Hide();
    }

    internal void SetExpanded(bool value)
    {
        expanded = value;
        if (value)
        {
            editor.Show();
            ArrangeEditor();
        }
        reveal.SpringTo(value ? 1 : 0, 220);
        if (!reveal.IsRunning && !value) editor.Hide();
    }

    private void ArrangeEditor()
    {
        if (arranging || editor == null) return;
        arranging = true;
        try
        {
            editor.Width = ClientSize.Width;
            fullHeight = editor.GetPreferredSize(new Size(ClientSize.Width, 0)).Height;
            editor.Height = fullHeight;
            arrangedWidth = Width;
        }
        finally { arranging = false; }
    }

    private void ApplyHeight()
    {
        long started = Stopwatch.GetTimestamp();
        int height = (int)Math.Round(fullHeight * reveal.Value);
        if (height != Height || stackDirty && !reveal.IsRunning)
        {
            SuspendLayout();
            for (Control? parent = Parent; parent is TableLayoutPanel; parent = parent.Parent)
                parent.SuspendLayout();
            bool moveStack = reveal.IsRunning && CanMoveStack();
            try
            {
                if (moveStack) MoveStack(height);
                else Height = height;
            }
            finally
            {
                // Batch the auto-sized stack as one height change. Resuming the inner
                // tables separately repeats the entire ancestor layout for one frame.
                ResumeLayout(false);
                for (Control? parent = Parent; parent is TableLayoutPanel; parent = parent.Parent)
                    parent.ResumeLayout(!moveStack);
                stackDirty = moveStack;
            }
            long laidOut = Stopwatch.GetTimestamp();
            LayoutTicks += laidOut - started;
            if (Visible) PresentFrame?.Invoke();
            PresentTicks += Stopwatch.GetTimestamp() - laidOut;
        }
        UpdateTicks += Stopwatch.GetTimestamp() - started;
        UpdatedFrames++;
    }

    private bool CanMoveStack()
    {
        if (Parent is not TableLayoutPanel) return false;
        for (Control? parent = Parent; parent is TableLayoutPanel table; parent = parent.Parent)
            if (table.ColumnCount != 1 || table.Dock != DockStyle.Top || !table.AutoSize ||
                table.MinimumSize.Height != 0 || table.MaximumSize.Height != 0) return false;
        return true;
    }

    private void MoveStack(int height)
    {
        int delta = height - Height;
        int previousBottom = Bottom;
        Height = height;
        Control child = this;
        for (Control? parent = Parent; parent is TableLayoutPanel; parent = parent.Parent)
        {
            // Only the revealed row changes height. Keep the already measured native
            // controls at their existing sizes and translate the following rows together.
            foreach (Control sibling in parent.Controls)
                if (sibling != child && sibling.Visible && sibling.Top >= previousBottom)
                    sibling.Top += delta;
            previousBottom = parent.Bottom;
            parent.Height += delta;
            child = parent;
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        // Height changes are clipping only. The editor is measured on width/content changes.
        if (editor != null && Width != arrangedWidth)
        {
            ArrangeEditor();
            if (reveal != null) ApplyHeight();
        }
    }

    public override Size GetPreferredSize(Size proposedSize) => new(Width, Height);

    internal void Finish()
    {
        reveal.SnapTo(expanded ? 1 : 0);
        if (!expanded) editor.Hide();
    }
}
