using ShareX.Roll;
using System.Reflection;

internal static class SettingsMotionContinuityRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    internal static async Task RunAsync(MainForm main)
    {
        bool previousMotion = UiMotion.UserEnabled;
        using Form window = new() { Text = "SnapFlow wheel continuity", ClientSize = new Size(640, 420), StartPosition = FormStartPosition.CenterParent };
        using SettingsScrollViewport viewport = new() { AutoScroll = true };
        using SettingsScrollHost host = new(viewport) { Dock = DockStyle.Fill };
        window.Controls.Add(host);
        viewport.Controls.Add(new Panel { Size = new Size(400, 12000) });
        try
        {
            UiMotion.UserEnabled = true;
            window.Show(main);
            window.Refresh();
            await Task.Delay(80);
            if (!UiMotion.Enabled || SystemInformation.MouseWheelScrollLines == 0)
            {
                Console.WriteLine("SKIP: delayed wheel retargeting requires enabled Windows animation and wheel scrolling");
                return;
            }

            int maximum = Math.Max(0, viewport.DisplayRectangle.Height - viewport.ClientSize.Height);
            MethodInfo advance = typeof(SettingsScrollHost).GetMethod("Advance", Private)!;
            FieldInfo targetField = typeof(SettingsScrollHost).GetField("target", Private)!;
            viewport.AutoScrollPosition = new Point(0, maximum / 2);
            host.ScrollWheel(-120);
            // Hold the UI thread as a busy native operation would, without letting the
            // pending frame run. Reversal must redirect the last visible position.
            Thread.Sleep(110);
            host.ScrollWheel(120);
            int afterInput = -viewport.AutoScrollPosition.Y;
            double destination = (double)targetField.GetValue(host)!;
            advance.Invoke(host, null);
            int afterFrame = -viewport.AutoScrollPosition.Y;
            double remaining = Math.Abs(destination - afterInput);
            Check(Math.Abs(afterFrame - afterInput) <= Math.Max(2, remaining * .15),
                "reversed wheel input does not replay old waiting time toward its new destination");
            await Task.Delay(420);
            Check(!host.IsScrolling && -viewport.AutoScrollPosition.Y == (int)Math.Round(destination),
                "reversed delayed wheel input still settles at the exact native scroll destination");
        }
        finally { UiMotion.UserEnabled = previousMotion; }
    }
}
