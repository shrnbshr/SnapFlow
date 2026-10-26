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
    private readonly System.Windows.Forms.Timer? timer;
    private readonly ScrollFrameClock? frameClock;
    private readonly Stopwatch clock = new();
    private readonly Action changed;
    private float from, target, velocity;
    private double lastSample, frequency;
    private int duration, revision;
    private bool disposed, spring;
    internal float Value { get; private set; }
    internal float Velocity => velocity;
    internal bool IsRunning => frameClock?.Enabled ?? timer?.Enabled == true;
    internal event Action? Completed;

    internal UiTween(Control owner, Action changed, bool useFrameClock = true)
    {
        this.owner = owner;
        this.changed = changed;
        if (useFrameClock) frameClock = new ScrollFrameClock(owner, Tick);
        else
        {
            timer = new System.Windows.Forms.Timer { Interval = 15 };
            timer.Tick += (_, _) => Tick();
        }
        owner.Disposed += (_, _) => Dispose();
        owner.VisibleChanged += (_, _) => { if (!owner.Visible) SnapTo(target); };
        owner.HandleDestroyed += (_, _) => SnapTo(target);
    }

    internal void AnimateTo(float value, int milliseconds = 140)
    {
        if (disposed) return;
        if (!UiMotion.Enabled || !owner.Visible || !owner.IsHandleCreated || milliseconds <= 0)
        {
            CompleteAt(value);
            return;
        }
        if (IsRunning && !spring && target == value && duration == milliseconds) return;
        revision++;
        spring = false;
        velocity = 0;
        from = Value;
        target = value;
        duration = milliseconds;
        if (Math.Abs(target - from) < .001f) { SnapTo(value); return; }
        clock.Restart();
        StartClock();
    }

    // Interactive feedback retains its on-screen position and velocity when the target changes.
    // Critical damping avoids decorative bounce; values stay inside the color/opacity range.
    internal void SpringTo(float value, int responseMilliseconds = 140)
    {
        if (disposed) return;
        value = Math.Clamp(value, 0, 1);
        if (!UiMotion.Enabled || !owner.Visible || !owner.IsHandleCreated || responseMilliseconds <= 0)
        {
            CompleteAt(value);
            return;
        }
        if (IsRunning && spring && target == value && duration == responseMilliseconds) return;
        revision++;
        if (!spring || !IsRunning) velocity = 0;
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
        StartClock();
    }

    internal void SnapTo(float value)
    {
        if (disposed) return;
        revision++;
        StopClock();
        clock.Stop();
        velocity = 0;
        Value = target = value;
        changed();
    }

    private void Tick()
    {
        if (disposed || owner.IsDisposed) { Dispose(); return; }
        if (!owner.Visible) { Complete(); return; }
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
        if (progress >= 1) { Complete(); return; }
        float eased = 1 - MathF.Pow(1 - progress, 3);
        Value = from + (target - from) * eased;
        changed();
    }

    private void Complete() => CompleteAt(target);

    private void CompleteAt(float value)
    {
        int completedRevision = revision + 1;
        SnapTo(value);
        // A final-frame layout/paint callback can immediately start a new animation.
        // Its completion belongs to that new run, rather than the run just stopped.
        if (!disposed && revision == completedRevision) Completed?.Invoke();
    }

    private void StartClock() { if (frameClock != null) frameClock.Start(); else timer?.Start(); }
    private void StopClock() { frameClock?.Stop(); timer?.Stop(); }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        revision++;
        timer?.Stop();
        clock.Stop();
        timer?.Dispose();
        frameClock?.Dispose();
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
    internal (int BeforeX, int AfterX, float Opacity) MaterialPlacement =>
        ((int)(-direction * progress.Value * 8 * (DeviceDpi / 96f)),
            (int)(direction * (1 - progress.Value) * 18 * (DeviceDpi / 96f)), progress.Value);

    internal PageTransition(Bitmap before, Color background, bool forward, bool translate = true)
    {
        this.before = new MotionFrame(before);
        surface = new MotionFrame(this.before.Size);
        direction = translate ? (forward ? 1 : -1) : 0;
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
            windowBackdrop?.RememberSnapshot(page, bitmap);
            return bitmap;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ExternalException)
        {
            bitmap.Dispose();
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);

    private Bitmap? SnapshotPresentedPage(Control page)
    {
        if (!UiMotion.Enabled || !Visible || !page.Visible || page.Width <= 0 || page.Height <= 0 ||
            ShareX.HelpersLib.NativeMethods.GetForegroundWindow() != Handle) return null;
        Bitmap? bitmap = null;
        try
        {
            Point first = page.PointToScreen(Point.Empty), last = page.PointToScreen(new Point(page.Width, page.Height));
            if (!LogicalToPhysicalPointForPerMonitorDPI(page.Handle, ref first) || !LogicalToPhysicalPointForPerMonitorDPI(page.Handle, ref last)) return null;
            using Bitmap captured = new ShareX.ScreenCaptureLib.Screenshot { RemoveOutsideScreenArea = false }
                .CaptureRectangle(Rectangle.FromLTRB(first.X, first.Y, last.X, last.Y));
            bitmap = captured.Size == page.Size ? (Bitmap)captured.Clone() : new Bitmap(captured, page.Size);
            RemoveOccludingWindows(bitmap, page, Rectangle.FromLTRB(first.X, first.Y, last.X, last.Y));
            windowBackdrop?.RememberOpaqueSnapshot(bitmap);
            Bitmap result = bitmap;
            bitmap = null;
            return result;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ExternalException) { return null; }
        finally { bitmap?.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PresentedRectangle { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern nint WindowFromPhysicalPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out PresentedRectangle value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetWindowInteger(nint window, int attribute, out int value, int size);

    private void RemoveOccludingWindows(Bitmap bitmap, Control page, Rectangle physicalBounds)
    {
        Bitmap? own = null;
        List<Rectangle> replacements = new();
        nint previousDpi = SetThreadDpiAwarenessContext(-4);
        if (previousDpi == 0) throw new InvalidOperationException("Physical window coordinates are unavailable.");
        try
        {
            for (nint window = GetWindow(Handle, 3); window != 0; window = GetWindow(window, 3))
            {
                if (!IsWindowVisible(window) || DwmGetWindowInteger(window, 14, out int cloaked, sizeof(int)) >= 0 && cloaked != 0 ||
                    DwmGetWindowAttribute(window, 9, out PresentedRectangle bounds, Marshal.SizeOf<PresentedRectangle>()) < 0) continue;
                Rectangle covered = Rectangle.Intersect(physicalBounds,
                    Rectangle.Inflate(Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), 12, 12));
                if (covered.IsEmpty) continue;
                float sx = (float)bitmap.Width / physicalBounds.Width, sy = (float)bitmap.Height / physicalBounds.Height;
                Rectangle area = Rectangle.Intersect(new Rectangle(Point.Empty, bitmap.Size), Rectangle.FromLTRB(
                    (int)Math.Floor((covered.Left - physicalBounds.Left) * sx), (int)Math.Floor((covered.Top - physicalBounds.Top) * sy),
                    (int)Math.Ceiling((covered.Right - physicalBounds.Left) * sx), (int)Math.Ceiling((covered.Bottom - physicalBounds.Top) * sy)));
                if (area.Width <= 0 || area.Height <= 0) continue;
                for (int y = area.Top; y < area.Bottom; y += 8)
                {
                    int start = -1, bottom = Math.Min(y + 8, area.Bottom);
                    for (int x = area.Left; x <= area.Right; x += 8)
                    {
                        int right = Math.Min(x + 8, area.Right);
                        Point point = new(physicalBounds.Left + (int)((x + right) / 2f / sx),
                            physicalBounds.Top + (int)((y + bottom) / 2f / sy));
                        bool foreign = x < area.Right && GetAncestor(WindowFromPhysicalPoint(point), 2) != Handle;
                        if (foreign && start < 0) start = x;
                        if (!foreign && start >= 0)
                        {
                            replacements.Add(Rectangle.FromLTRB(start, y, Math.Min(x, area.Right), bottom)); start = -1;
                        }
                        if (right == area.Right)
                        {
                            if (start >= 0) replacements.Add(Rectangle.FromLTRB(start, y, area.Right, bottom));
                            break;
                        }
                    }
                }
            }
        }
        finally { if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi); }
        if (replacements.Count == 0) return;
        try
        {
            own = SnapshotPage(page);
            if (own == null) throw new InvalidOperationException("The occluded application image is unavailable.");
            foreach (Rectangle replacement in replacements) CopyOpaqueRegion(own, bitmap, replacement);
        }
        finally { own?.Dispose(); }
    }

    private static void CopyOpaqueRegion(Bitmap own, Bitmap bitmap, Rectangle area)
    {
        var input = own.LockBits(area, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var output = bitmap.LockBits(area, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int[] row = new int[area.Width];
                for (int y = 0; y < area.Height; y++)
                {
                    Marshal.Copy(nint.Add(input.Scan0, y * input.Stride), row, 0, row.Length);
                    for (int x = 0; x < row.Length; x++) row[x] |= unchecked((int)0xFF000000);
                    Marshal.Copy(row, 0, nint.Add(output.Scan0, y * output.Stride), row.Length);
                }
            }
            finally { bitmap.UnlockBits(output); }
        }
        finally { own.UnlockBits(input); }
    }

    private PageTransition? PreparePageChange(Control page, bool forward, bool crossFade = false)
    {
        PageTransition? previous = pageTransition;
        // Keep the current visual, including an interrupted animation, over the live pages.
        Bitmap? before = crossFade ? SnapshotPresentedPage(previous ?? page) ?? SnapshotPage(previous ?? page) : SnapshotPage(previous ?? page);
        if (before == null) { EndPageTransition(); return null; }
        PageTransition transition = new(before, BackColor, forward, translate: !crossFade)
        {
            Name = "pageTransition", Bounds = ClientRectangle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        pageTransition = transition;
        windowBackdrop?.BeginTransition(transition, before);
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

    private void AnimatePageChange(PageTransition? transition, Control page, bool cacheSettings = false)
    {
        if (transition == null || !ReferenceEquals(pageTransition, transition)) return;
        Bitmap? after = cacheSettings ? SettingsSnapshot(page) : null;
        if (after == null)
        {
            after = SnapshotPage(page);
            if (cacheSettings && after != null) RememberSettingsSnapshot(page, after);
        }
        if (after == null) { EndPageTransition(); return; }
        transition.Start(after);
        windowBackdrop?.CompleteTransition(transition, after);
    }
}
