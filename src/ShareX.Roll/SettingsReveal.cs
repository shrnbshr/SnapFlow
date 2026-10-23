// GPL-3.0-or-later. Clip native child windows without remeasuring their controls per frame.
using System.Diagnostics;

namespace ShareX.Roll;

internal sealed class SettingsReveal : Panel
{
    private readonly ScrollingOptionsEditor editor;
    private readonly UiTween reveal;
    private int fullHeight, arrangedWidth;
    private bool expanded, arranging;
    internal bool IsAnimating => reveal.IsRunning;
    internal float CurrentHeight => Height;
    internal long UpdateTicks { get; private set; }
    internal int UpdatedFrames { get; private set; }

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
        reveal = new UiTween(this, ApplyHeight);
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
        Height = (int)Math.Round(fullHeight * reveal.Value);
        UpdateTicks += Stopwatch.GetTimestamp() - started;
        UpdatedFrames++;
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
