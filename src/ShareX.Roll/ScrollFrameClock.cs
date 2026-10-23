// GPL-3.0-or-later. An active-only frame clock; all control access stays on the UI thread.
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace ShareX.Roll;

internal sealed class ScrollFrameClock(Control owner, Action tick) : IDisposable
{
    private Run? active;
    internal bool Enabled => active != null;

    internal void Start()
    {
        if (active != null || owner.IsDisposed || !owner.IsHandleCreated) return;
        active = new Run(owner, tick);
        ThreadPool.QueueUserWorkItem(static state => ((Run)state!).Pump(), active);
    }

    internal void Stop()
    {
        if (active is { } run) run.Cancelled = true;
        active = null;
    }

    public void Dispose() => Stop();

    private sealed class Run(Control owner, Action tick)
    {
        internal volatile bool Cancelled;
        private int pending;

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
                    owner.BeginInvoke((Action)(() =>
                    {
                        try { if (!Cancelled && !owner.IsDisposed) tick(); }
                        finally { Interlocked.Exchange(ref pending, 0); }
                    }));
                }
                catch (InvalidOperationException) { Cancelled = true; }
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
