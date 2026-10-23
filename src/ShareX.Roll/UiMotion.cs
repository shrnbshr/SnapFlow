// GPL-3.0-or-later. Short, cancellable UI animations; no capture scheduling or image processing.
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShareX.Roll;

internal static class UiMotion
{
    internal static bool UserEnabled { get; set; } = true;
    internal static bool Enabled
    {
        get
        {
            if (!UserEnabled || SystemInformation.HighContrast) return false;
            return SystemParametersInfo(0x1042, 0, out int enabled, 0)
                ? enabled != 0 : SystemInformation.UIEffectsEnabled;
        }
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);
}

internal sealed class UiTween : IDisposable
{
    private readonly Control owner;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    private readonly Stopwatch clock = new();
    private readonly Action changed;
    private float from, target, velocity;
    private double lastSample, frequency;
    private int duration;
    private bool disposed, spring;
    internal float Value { get; private set; }
    internal float Velocity => velocity;
    internal bool IsRunning => timer.Enabled;
    internal event Action? Completed;

    internal UiTween(Control owner, Action changed)
    {
        this.owner = owner;
        this.changed = changed;
        timer.Tick += (_, _) => Tick();
        owner.Disposed += (_, _) => Dispose();
        owner.VisibleChanged += (_, _) => { if (!owner.Visible) SnapTo(target); };
        owner.HandleDestroyed += (_, _) => SnapTo(target);
    }

    internal void AnimateTo(float value, int milliseconds = 140)
    {
        if (disposed) return;
        if (!UiMotion.Enabled || !owner.Visible || !owner.IsHandleCreated || milliseconds <= 0)
        {
            SnapTo(value);
            Completed?.Invoke();
            return;
        }
        if (timer.Enabled && !spring && target == value && duration == milliseconds) return;
        spring = false;
        velocity = 0;
        from = Value;
        target = value;
        duration = milliseconds;
        if (Math.Abs(target - from) < .001f) { SnapTo(value); return; }
        clock.Restart();
        timer.Start();
    }

    // Interactive feedback retains its on-screen position and velocity when the target changes.
    // Critical damping avoids decorative bounce; values stay inside the color/opacity range.
    internal void SpringTo(float value, int responseMilliseconds = 140)
    {
        if (disposed) return;
        value = Math.Clamp(value, 0, 1);
        if (!UiMotion.Enabled || !owner.Visible || !owner.IsHandleCreated || responseMilliseconds <= 0)
        {
            SnapTo(value);
            Completed?.Invoke();
            return;
        }
        if (timer.Enabled && spring && target == value) return;
        if (!spring || !timer.Enabled) velocity = 0;
        spring = true;
        target = value;
        duration = responseMilliseconds;
        frequency = 7d / (responseMilliseconds / 1000d);
        if (Math.Abs(target - Value) < .001f && Math.Abs(velocity) < .05f)
        {
            SnapTo(value);
            return;
        }
        lastSample = 0;
        clock.Restart();
        timer.Start();
    }

    internal void SnapTo(float value)
    {
        if (disposed) return;
        timer.Stop();
        clock.Stop();
        velocity = 0;
        Value = target = value;
        changed();
    }

    private void Tick()
    {
        if (disposed || owner.IsDisposed) { Dispose(); return; }
        if (spring)
        {
            if (!UiMotion.Enabled) { Complete(); return; }
            double elapsed = clock.Elapsed.TotalSeconds;
            double seconds = Math.Max(0, elapsed - lastSample);
            lastSample = elapsed;
            double displacement = Value - target;
            double coefficient = velocity + frequency * displacement;
            double decay = Math.Exp(-frequency * seconds);
            double next = target + (displacement + coefficient * seconds) * decay;
            velocity = (float)((velocity - frequency * coefficient * seconds) * decay);
            Value = (float)Math.Clamp(next, 0, 1);
            if (next < 0 || next > 1) velocity = 0;
            if ((Math.Abs(Value - target) < .001f && Math.Abs(velocity) < .05f)
                || elapsed * 1000 >= Math.Max(300, duration * 3))
            {
                Complete();
                return;
            }
            changed();
            return;
        }
        float progress = !UiMotion.Enabled ? 1 : Math.Clamp((float)clock.Elapsed.TotalMilliseconds / duration, 0, 1);
        float eased = 1 - MathF.Pow(1 - progress, 3);
        Value = from + (target - from) * eased;
        changed();
        if (progress >= 1) { timer.Stop(); clock.Stop(); Completed?.Invoke(); }
    }

    private void Complete()
    {
        SnapTo(target);
        Completed?.Invoke();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        clock.Stop();
        timer.Dispose();
    }
}

internal sealed class PageTransition : Control
{
    private readonly MotionFrame before;
    private readonly MotionFrame surface;
    private MotionFrame? after;
    private readonly UiTween progress;
    private readonly int direction;
    internal event Action? Finished;
    internal bool IsAnimating => progress.IsRunning;

    internal PageTransition(Bitmap before, Color background, bool forward)
    {
        this.before = new MotionFrame(before);
        surface = new MotionFrame(this.before.Size);
        direction = forward ? 1 : -1;
        BackColor = background;
        TabStop = false;
        AccessibleRole = AccessibleRole.None;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
        progress = new(this, Invalidate);
        progress.Completed += () => Finished?.Invoke();
        VisibleChanged += (_, _) => { if (!Visible) Finished?.Invoke(); };
        SizeChanged += (_, _) => { if (IsHandleCreated && progress.IsRunning) Finished?.Invoke(); };
    }

    internal void Start(Bitmap destination)
    {
        after?.Dispose();
        after = new MotionFrame(destination);
        progress.AnimateTo(1, 200);
    }

    protected override void WndProc(ref Message message)
    {
        // The real page is already interactive while the visual transition plays.
        if (message.Msg == 0x84) { message.Result = new IntPtr(-1); return; }
        base.WndProc(ref message);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float value = progress.Value;
        float scale = DeviceDpi / 96f;
        surface.Compose(e.Graphics, BackColor, before, (int)(-direction * value * 8 * scale),
            second: after, secondX: (int)(direction * (1 - value) * 18 * scale), secondOpacity: value);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { progress.Dispose(); surface.Dispose(); before.Dispose(); after?.Dispose(); }
        base.Dispose(disposing);
    }
}

internal static class TransitionSnapshot
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle { public int Left, Top, Right, Bottom; }

    // https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-comboboxinfo
    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo
    {
        public int Size;
        public NativeRectangle Item, Button;
        public int ButtonState;
        public IntPtr ComboBox, ItemWindow, ListWindow;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetComboBoxInfo(IntPtr handle, ref ComboBoxInfo info);

    internal static void RestoreNativeTextColors(Control page, Bitmap bitmap)
    {
        using Graphics graphics = Graphics.FromImage(bitmap);
        void Visit(Control control, Rectangle clip)
        {
            if (!control.Visible) return;
            Point origin = page.PointToClient(control.PointToScreen(Point.Empty));
            clip.Intersect(new Rectangle(origin, control.ClientSize));
            if (clip.IsEmpty) return;
            if (control is ComboBox { DropDownStyle: ComboBoxStyle.DropDownList, FlatStyle: FlatStyle.Flat, DrawMode: DrawMode.Normal } combo && combo is not SettingsChoice)
            {
                ComboBoxInfo info = new() { Size = Marshal.SizeOf<ComboBoxInfo>() };
                if (GetComboBoxInfo(combo.Handle, ref info))
                {
                    // WM_PRINT renders this native text slot with system window colors.
                    // Correct only its transition image; leave the live combo and its behavior alone.
                    Rectangle item = Rectangle.FromLTRB(origin.X + info.Item.Left, origin.Y + info.Item.Top,
                        origin.X + info.Item.Right, origin.Y + info.Item.Bottom);
                    System.Drawing.Drawing2D.GraphicsState saved = graphics.Save();
                    graphics.SetClip(clip);
                    using SolidBrush background = new(combo.BackColor);
                    graphics.FillRectangle(background, item);
                    item.Inflate(-1, 0);
                    TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;
                    if (combo.RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
                    TextRenderer.DrawText(graphics, combo.Text, combo.Font, item, combo.ForeColor, flags);
                    graphics.Restore(saved);
                }
            }
            foreach (Control child in control.Controls) Visit(child, clip);
        }
        Visit(page, new Rectangle(Point.Empty, bitmap.Size));
    }
}

public sealed partial class MainForm
{
    private PageTransition? pageTransition;

    private void EndPageTransition()
    {
        PageTransition? transition = pageTransition;
        pageTransition = null;
        transition?.Dispose();
    }

    private Bitmap? SnapshotPage(Control page)
    {
        if (!UiMotion.Enabled || !Visible || !page.Visible || page.Width <= 0 || page.Height <= 0) return null;
        Bitmap bitmap = new(page.Width, page.Height);
        try
        {
            page.DrawToBitmap(bitmap, new Rectangle(Point.Empty, page.Size));
            TransitionSnapshot.RestoreNativeTextColors(page, bitmap);
            return bitmap;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ExternalException)
        {
            bitmap.Dispose();
            return null;
        }
    }

    private PageTransition? PreparePageChange(Control page, bool forward)
    {
        PageTransition? previous = pageTransition;
        // Keep the current visual, including an interrupted animation, over the live pages.
        Bitmap? before = SnapshotPage(previous ?? page);
        if (before == null) { EndPageTransition(); return null; }
        PageTransition transition = new(before, BackColor, forward)
        {
            Name = "pageTransition", Bounds = ClientRectangle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        pageTransition = transition;
        transition.Finished += () =>
        {
            if (ReferenceEquals(pageTransition, transition)) pageTransition = null;
            transition.Dispose();
        };
        Controls.Add(transition);
        transition.BringToFront();
        // Present the cover before hiding, creating or disposing any page controls.
        transition.Refresh();
        previous?.Dispose();
        return transition;
    }

    private void AnimatePageChange(PageTransition? transition, Control page)
    {
        if (transition == null || !ReferenceEquals(pageTransition, transition)) return;
        Bitmap? after = SnapshotPage(page);
        if (after == null) { EndPageTransition(); return; }
        transition.Start(after);
    }
}
