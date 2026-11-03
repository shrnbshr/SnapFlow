using ShareX.Roll;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class SettingsKeyboardMotionRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static T Find<T>(Control parent, string name) where T : Control => (T)parent.Controls.Find(name, true).Single();
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private static void Key(Control control, Keys key)
    {
        // Match the application's preprocessing and native dispatch rather than calling OnClick.
        Message down = Message.Create(control.Handle, 0x0100, (nint)key, 1);
        if (!control.PreProcessMessage(ref down)) SendMessage(control.Handle, 0x0100, (nint)key, 1);
        Message up = Message.Create(control.Handle, 0x0101, (nint)key, unchecked((nint)0xC0000001));
        if (!control.PreProcessMessage(ref up)) SendMessage(control.Handle, 0x0101, (nint)key, unchecked((nint)0xC0000001));
    }

    private static void Mouse(Control control)
    {
        nint point = (nint)((control.Height / 2 << 16) | (control.Width / 2 & 0xFFFF));
        SendMessage(control.Handle, 0x0201, 1, point);
        SendMessage(control.Handle, 0x0202, 0, point);
    }

    internal static async Task RunAsync(MainForm main)
    {
        bool originalMotion = UiMotion.UserEnabled;
        using Form host = new()
        {
            Text = "SnapFlow settings keyboard motion", ClientSize = new Size(690, 760),
            AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi,
            StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false
        };
        using SettingsPage page = new(new() { AnimationsEnabled = true }, new(), (_, _) => null) { Dock = DockStyle.Fill };
        host.Controls.Add(page);
        try
        {
            UiMotion.UserEnabled = true;
            host.Show(main);
            await Task.Delay(100);
            SettingsDisclosure header = Find<SettingsDisclosure>(page, "scrollingOptionsButton");
            SettingsReveal reveal = Find<SettingsReveal>(page, "scrollingReveal");
            ScrollingOptionsEditor editor = Field<ScrollingOptionsEditor>(reveal, "editor");
            UiTween rotation = Field<UiTween>(header, "rotation"), height = Field<UiTween>(reveal, "reveal");
            Panel viewport = Field<Panel>(page, "scroller");
            viewport.ScrollControlIntoView(header);
            Check(header.Focus(), "scroll disclosure receives native keyboard focus");
            int clicks = 0;
            header.Click += (_, _) => clicks++;

            void Instant(bool expanded, string context, bool immediateScrollbar = true)
            {
                Check(header.Expanded == expanded, context + ": native activation toggles the disclosure");
                Check(!rotation.IsRunning && rotation.Value == (expanded ? 1 : 0), context + ": arrow reaches its exact final angle immediately");
                Check(!reveal.IsAnimating && height.Value == (expanded ? 1 : 0), context + ": native content reaches its final reveal immediately");
                Check(reveal.Height == (expanded ? Field<int>(reveal, "fullHeight") : 0), context + ": content clipping matches the final state");
                Check(editor.Visible == expanded, context + ": collapsed native editors are hidden");
                Check(!header.KeyboardActivation, context + ": keyboard source does not leak into later input");
                Control track = Field<Control>(Field<SettingsScrollHost>(page, "scrollHost"), "bar");
                if (immediateScrollbar)
                    Check(!Field<UiTween>(track, "geometry").IsRunning, context + ": scrollbar geometry also reaches its endpoint");
            }

            foreach (Keys key in new[] { Keys.Space, Keys.Enter })
            {
                int before = clicks;
                Key(header, key);
                Check(clicks == before + 1, key + ": native key dispatch raises Click once");
                Instant(true, key + " expands");
                Key(header, key);
                Check(clicks == before + 2, key + ": repeated dispatch raises one further Click");
                Instant(false, key + " collapses");
            }

            Mouse(header);
            if (UiMotion.Enabled)
            {
                Check(header.Expanded && rotation.IsRunning && reveal.IsAnimating, "mouse activation keeps arrow and content animation");
                Check(Field<int>(rotation, "duration") == 180 && Field<int>(height, "duration") == 220,
                    "mouse activation preserves the existing 180/220 ms spring response parameters");
                await Task.Delay(65);
                Check(reveal.Height > 0 && reveal.Height < Field<int>(reveal, "fullHeight"), "mouse expansion renders an intermediate native clipping height");
                float value = height.Value, velocity = height.Velocity, angle = rotation.Value, angularVelocity = rotation.Velocity;
                int current = reveal.Height;
                Mouse(header);
                Check(!header.Expanded && reveal.Height == current && height.Value == value && height.Velocity == velocity,
                    "mouse reversal retains the visible height and its velocity");
                Check(rotation.Value == angle && rotation.Velocity == angularVelocity, "mouse reversal retains the visible arrow and its velocity");
                Mouse(header);
                Check(header.Expanded && reveal.Height == current && height.Value == value, "immediate mouse reentry does not restart from an endpoint");
                Stopwatch settling = Stopwatch.StartNew();
                while ((reveal.IsAnimating || rotation.IsRunning) && settling.ElapsedMilliseconds < 2000) await Task.Delay(20);
                Instant(true, "mouse settles", immediateScrollbar: false);

                Mouse(header);
                await Task.Delay(45);
                Check(reveal.IsAnimating, "mouse collapse is still active before keyboard takeover");
                Key(header, Keys.Space);
                Instant(true, "Space interrupts mouse collapse");
                Key(header, Keys.Enter);
                Instant(false, "Enter closes after keyboard takeover");
            }
            else
            {
                Console.WriteLine("SKIP: intermediate mouse frames require enabled Windows animations");
                Instant(true, "system reduced motion expands");
                Key(header, Keys.Space);
                Instant(false, "system reduced motion collapses");
            }

            UiMotion.UserEnabled = false;
            Mouse(header);
            Instant(true, "app reduced motion expands");
            Mouse(header);
            Instant(false, "app reduced motion collapses");
            UiMotion.UserEnabled = true;
            await VerifyFolderFocus(main);
        }
        finally { UiMotion.UserEnabled = originalMotion; }
    }

    private static async Task VerifyFolderFocus(MainForm main)
    {
        using Form host = new()
        {
            Text = "SnapFlow folder focus motion", ClientSize = new Size(500, 140),
            StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false
        };
        using TextBox text = new() { Text = AppContext.BaseDirectory, ReadOnly = true };
        using SettingsFolderField field = new(text) { Bounds = new Rectangle(12, 12, 470, 44) };
        using Button next = new() { Text = "Focus target", Bounds = new Rectangle(12, 75, 140, 32) };
        host.Controls.Add(field);
        host.Controls.Add(next);
        host.Show(main);
        Check(next.Focus() && text.Focus(), "folder text field receives real native focus");
        UiTween focus = Field<UiTween>(field, "focus");
        await Task.Delay(35);
        if (UiMotion.Enabled) Check(focus.IsRunning && focus.Value > 0, "folder focus-in keeps its existing visible color transition");
        Check(next.Focus(), "native focus moves away from the folder field");
        Check(!focus.IsRunning && focus.Value == 0 && !field.ContainsFocus, "folder focus loss stops its invisible animation synchronously");
        await Task.Delay(140);
        Check(!focus.IsRunning && focus.Value == 0, "folder focus loss leaves no later animation callback active");
    }
}
