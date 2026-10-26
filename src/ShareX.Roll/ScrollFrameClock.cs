// GPL-3.0-or-later. An active-only frame clock; all control access stays on the UI thread.
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace ShareX.Roll;

internal sealed class ScrollFrameClock : IDisposable
{
    [ThreadStatic] private static Run? shared;
    private readonly Control owner;
    private readonly Action tick;
    private Run? active;
    private bool disposed;
    internal bool Enabled => active != null;

    internal ScrollFrameClock(Control owner, Action tick)
    {
        this.owner = owner;
        this.tick = tick;
    }

    internal void Start()
    {
        if (disposed || active != null || owner.IsDisposed || !owner.IsHandleCreated) return;
        Run? run = shared;
        bool start = run == null;
        if (start) shared = run = new Run();
        active = run;
        run!.Add(this);
        if (start) ThreadPool.QueueUserWorkItem(static state => ((Run)state!).Pump(), run);
    }

    internal void Stop()
    {
        Run? run = active;
        active = null;
        if (run == null) return;
        run.Remove(this);
        if (run.Cancelled && ReferenceEquals(shared, run)) shared = null;
    }

    public void Dispose() { Stop(); disposed = true; }

    // All clocks registered on one UI thread share one worker and at most one
    // queued callback. Concurrent hover, scrolling and zoom never grow the queue.
    private sealed class Run
    {
        internal volatile bool Cancelled;
        private readonly List<ScrollFrameClock> clocks = new();
        private readonly List<ScrollFrameClock> frame = new();
        private readonly WindowsFormsSynchronizationContext context =
            SynchronizationContext.Current as WindowsFormsSynchronizationContext ?? new WindowsFormsSynchronizationContext();
        private int pending;

        internal void Add(ScrollFrameClock clock)
        {
            clocks.Add(clock);
        }

        internal void Remove(ScrollFrameClock clock)
        {
            clocks.Remove(clock);
            if (clocks.Count == 0) Cancelled = true;
        }

        private void Dispatch()
        {
            try
            {
                // Callbacks may remove themselves or hide/dispose other owners.
                // Reuse a stable batch so stop/restart cannot tick a clock twice.
                frame.Clear();
                frame.AddRange(clocks);
                foreach (ScrollFrameClock clock in frame)
                {
                    if (Cancelled) break;
                    if (!ReferenceEquals(clock.active, this)) continue;
                    if (clock.owner.IsDisposed || !clock.owner.IsHandleCreated) clock.Stop();
                    else
                    {
                        // Hiding an ancestor need not raise VisibleChanged on its children.
                        // Give the owner one final tick to settle and release visual resources.
                        clock.tick();
                        if (!clock.owner.Visible && ReferenceEquals(clock.active, this)) clock.Stop();
                    }
                }
            }
            finally { frame.Clear(); Interlocked.Exchange(ref pending, 0); }
        }

        internal void Pump()
        {
            using SafeWaitHandle timer = CreateTimer();
            long due = -80_000; // Relative 8 ms; no process-wide timer-resolution change.
            bool timed = !timer.IsInvalid && SetWaitableTimer(timer, ref due, 8, IntPtr.Zero, IntPtr.Zero, false);
            while (!Cancelled)
            {
                if (timed)
                {
                    uint result = WaitForSingleObject(timer, 50);
                    if (result == 0x102) continue; // Timeout: check cancellation without spinning.
                    if (result != 0) { timed = false; continue; }
                }
                else Thread.Sleep(8);
                if (Cancelled || Interlocked.CompareExchange(ref pending, 1, 0) != 0) continue;
                try
                {
                    // The UI thread's marshaling control survives individual owner handles.
                    context.Post(static state => ((Run)state!).Dispatch(), this);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.InvalidAsynchronousStateException)
                {
                    Cancelled = true;
                    Interlocked.Exchange(ref pending, 0);
                }
            }
        }

        private static SafeWaitHandle CreateTimer()
        {
            SafeWaitHandle timer = CreateWaitableTimerEx(IntPtr.Zero, null, 0x2, 0x00100002);
            if (!timer.IsInvalid) return timer;
            timer.Dispose();
            return CreateWaitableTimerEx(IntPtr.Zero, null, 0, 0x00100002);
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeWaitHandle CreateWaitableTimerEx(IntPtr attributes, string? name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, IntPtr callback, IntPtr argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
    }
}
