using ShareX.Roll;
using System.Reflection;

internal static class MotionContinuityRegression
{
    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Call(object target, string name, params object[] args) => target.GetType()
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    internal static async Task RunAsync(MainForm main)
    {
        bool oldMotion = UiMotion.UserEnabled;
        using Form host = new() { Text = "Motion continuity regression", ClientSize = new Size(420, 160) };
        using CaptureModeButton first = new() { Bounds = new Rectangle(10, 10, 180, 120) };
        using Panel second = new() { Bounds = new Rectangle(210, 10, 180, 120) };
        host.Controls.AddRange([first, second]);
        try
        {
            host.Show(main);
            UiMotion.UserEnabled = true;
            bool allowed = UiMotion.Enabled;

            // A real final-frame callback can reverse a tween before Tick returns.
            // Drive just that tick directly so the regression does not depend on timer jitter.
            UiTween? reversing = null;
            bool reversed = false;
            int completed = 0;
            using (reversing = new UiTween(host, () =>
            {
                if (reversing!.Value == 1 && !reversed)
                {
                    reversed = true;
                    reversing.AnimateTo(0, 140);
                }
            }, useFrameClock: false))
            {
                reversing.Completed += () => completed++;
                if (allowed)
                {
                    reversing.AnimateTo(1, 1);
                    Thread.Sleep(4);
                    Call(reversing, "Tick");
                    Check(reversed && reversing.IsRunning && reversing.Value == 1,
                        "a final-frame reversal retains its new animation clock");
                    Check(completed == 0, "a replaced run cannot publish a stale completion event");
                    reversing.SnapTo(0);
                }
            }

            UiMotion.UserEnabled = false;
            UiTween? immediate = null;
            reversed = false;
            completed = 0;
            using (immediate = new UiTween(host, () =>
            {
                if (immediate!.Value == 1 && !reversed)
                {
                    reversed = true;
                    immediate.SpringTo(0);
                }
            }))
            {
                immediate.Completed += () => completed++;
                immediate.AnimateTo(1);
                Check(immediate.Value == 0 && !immediate.IsRunning && completed == 1,
                    "reduced-motion reentrancy completes only the replacement state");
            }

            UiTween? disposing = null;
            completed = 0;
            using (disposing = new UiTween(host, () => disposing!.Dispose()))
            {
                disposing.Completed += () => completed++;
                disposing.SpringTo(1);
                Check(completed == 0 && !disposing.IsRunning,
                    "disposing from a final-state callback suppresses stale completion");
            }

            Call(first, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
            first.Capture = false;
            Call(first, "OnMouseCaptureChanged", EventArgs.Empty);
            Check(Field<UiTween>(first, "press").Value == 0,
                "losing pointer capture releases a capture button's pressed visual state");

            UiMotion.UserEnabled = true;
            if (!allowed) return;
            int firstFrames = 0, secondFrames = 0;
            using UiTween firstTween = new(first, () => firstFrames++);
            using UiTween secondTween = new(second, () => secondFrames++);
            firstTween.SpringTo(1, 300);
            secondTween.SpringTo(1, 300);
            ScrollFrameClock firstClock = Field<ScrollFrameClock>(firstTween, "frameClock");
            ScrollFrameClock secondClock = Field<ScrollFrameClock>(secondTween, "frameClock");
            Check(ReferenceEquals(Field<object>(firstClock, "active"), Field<object>(secondClock, "active")),
                "concurrent default tweens share one active UI-thread frame pump");
            first.Dispose();
            int previousFrames = secondFrames;
            await Task.Delay(45);
            Check(!firstTween.IsRunning && secondTween.IsRunning && secondFrames > previousFrames,
                "destroying the first owner leaves another owner's frame pump responsive");
            secondTween.SnapTo(0);

            VerifyBatchMutation(host);

            Call(firstTween, "Tick"); // Disposed-owner handling remains harmless for a queued frame.
            secondTween.SpringTo(1, 300);
            host.Hide();
            await Task.Delay(50);
            Check(!secondTween.IsRunning && secondTween.Value == 1,
                "hiding the last owner stops the shared clock and settles its visual state");
        }
        finally
        {
            UiMotion.UserEnabled = oldMotion;
            host.Close();
        }
    }

    private static void VerifyBatchMutation(Control owner)
    {
        int a = 0, b = 0, c = 0;
        ScrollFrameClock? first = null;
        using (first = new ScrollFrameClock(owner, () => { a++; first!.Stop(); first.Start(); }))
        using (ScrollFrameClock second = new(owner, () => b++))
        using (ScrollFrameClock third = new(owner, () => c++))
        {
            first.Start(); second.Start(); third.Start();
            object run = Field<object>(first, "active");
            Call(run, "Dispatch");
            Check(a == 1 && b == 1 && c == 1,
                "stop and restart during a frame ticks every original clock exactly once");
        }

        a = b = c = 0;
        using (ScrollFrameClock earlier = new(owner, () => a++))
        {
            ScrollFrameClock? removing = null;
            using (removing = new ScrollFrameClock(owner, () => { b++; earlier.Stop(); removing!.Stop(); }))
            using (ScrollFrameClock later = new(owner, () => c++))
            {
                earlier.Start(); removing.Start(); later.Start();
                Call(Field<object>(earlier, "active"), "Dispatch");
                Check(a == 1 && b == 1 && c == 1,
                    "removing earlier clocks during a frame does not skip a later clock");
            }
        }
    }
}
