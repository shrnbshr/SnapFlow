using ShareX.Roll;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

internal static class MotionRegression
{
    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Call(Control target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    internal static async Task RunAsync(MainForm main)
    {
        bool originalMotion = UiMotion.UserEnabled;
        using Form host = new() { Text = "SnapFlow motion test", ClientSize = new Size(540, 300), StartPosition = FormStartPosition.CenterScreen };
        using CaptureModeButton button = new() { Text = "Motion test", Bounds = new Rectangle(24, 24, 220, 72) };
        using Button action = new() { Text = "Action test", Bounds = new Rectangle(24, 112, 220, 40) };
        using MotionToggle toggle = new() { Bounds = new Rectangle(24, 176, 56, 32) };
        using PreviewCanvas preview = new() { Bounds = new Rectangle(270, 24, 240, 240), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
        host.Controls.AddRange([button, action, toggle, preview]);
        CaptureButtonPainter.Apply(action, CapturePalette.Light, false);
        string output = Path.GetFullPath("artifacts/motion-polish");
        Directory.CreateDirectory(output);
        try
        {
            UiMotion.UserEnabled = true;
            host.Show(main);
            host.Refresh();
            await Task.Delay(200);
            Call(button, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
            Check(Field<UiTween>(button, "press").Value == 1, "pointer-down feedback is visible before the next timer tick");
            Call(button, "OnKeyDown", new KeyEventArgs(Keys.Space));
            Check(Field<UiTween>(button, "press").Value == 1, "Space key has the same immediate pressed feedback");
            Call(button, "OnKeyUp", new KeyEventArgs(Keys.Space));
            int clicks = 0;
            button.Click += (_, _) => clicks++;
            button.PerformClick();
            Check(clicks == 1, "visual feedback leaves native click delivery synchronous and unchanged");

            string focusOutput = Path.GetFullPath(Path.Combine("artifacts/focus-ring-fix",
                Environment.GetEnvironmentVariable("SNAPFLOW_FOCUS_STAGE") ?? "after", main.DeviceDpi.ToString()));
            Directory.CreateDirectory(focusOutput);
            action.Text = "选项…";
            foreach ((string name, CapturePalette palette) in new[] { ("Light", CapturePalette.Light), ("Dark", CapturePalette.Dark) })
            {
                host.BackColor = palette.Background;
                palette.StyleButton(action);
                action.Focus();
                action.Refresh();
                using Bitmap focusFrame = new(action.Width, action.Height);
                action.DrawToBitmap(focusFrame, action.ClientRectangle);
                focusFrame.Save(Path.Combine(focusOutput, name + ".png"), ImageFormat.Png);
            }
            CapturePalette.Light.StyleButton(action);

            Call(action, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
            object table = typeof(CaptureButtonPainter).GetField("states", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            object?[] lookup = [action, null];
            table.GetType().GetMethod("TryGetValue")!.Invoke(table, lookup);
            UiTween actionPress = Field<UiTween>(lookup[1]!, "Press");
            Check(actionPress.Value == 1, "standard action buttons also respond before the next timer tick");
            action.Enabled = false;
            Check(actionPress.Value == 0 && !actionPress.IsRunning, "disabled buttons clear their pressed state and stop its timer");

            bool allowed = UiMotion.Enabled;
            Console.WriteLine("Windows permits motion: " + allowed);
            using UiTween spring = new(host, () => { });
            if (allowed)
            {
                spring.SpringTo(1);
                await Task.Delay(45);
                Console.WriteLine($"Spring sample: value={spring.Value:F4}, velocity={spring.Velocity:F4}, running={spring.IsRunning}, ownerVisible={host.Visible}");
                Check(spring.IsRunning && spring.Value > 0 && spring.Value < 1, "spring produces intermediate positions");
                float value = spring.Value, velocity = spring.Velocity;
                Check(velocity > 0, "spring is moving toward its first target");
                spring.SpringTo(0);
                Check(spring.Value == value && spring.Velocity == velocity, "reversing a moving spring preserves both position and velocity");
                for (int i = 0; i < 12; i++)
                {
                    spring.SpringTo(i % 2);
                    await Task.Delay(12);
                    Check(spring.Value >= 0 && spring.Value <= 1 && float.IsFinite(spring.Velocity), "rapid reversal stays within valid color and opacity bounds");
                }
                spring.SpringTo(0);
                Stopwatch settle = Stopwatch.StartNew();
                while (spring.IsRunning && settle.ElapsedMilliseconds < 1000) await Task.Delay(15);
                Check(spring.Value == 0 && !spring.IsRunning, "spring settles exactly and stops scheduling frames");

                toggle.Checked = true;
                await Task.Delay(35);
                UiTween position = Field<UiTween>(toggle, "position");
                float togglePosition = position.Value, toggleVelocity = position.Velocity;
                toggle.Checked = false;
                Check(position.Value == togglePosition && position.Velocity == toggleVelocity, "switch reversal inherits the current thumb motion");

                spring.SpringTo(1);
                UiMotion.UserEnabled = false;
                await Task.Delay(50);
                Check(spring.Value == 1 && !spring.IsRunning, "turning motion off completes an in-flight spring without leaving a timer");
                UiMotion.UserEnabled = true;
            }

            Call(button, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
            Call(button, "OnMouseEnter", EventArgs.Empty);
            button.Hide();
            Check(Field<UiTween>(button, "press").Value == 0 && !Field<UiTween>(button, "hover").IsRunning,
                "hiding a capture button clears pressed feedback and active hover motion");
            button.Show();
            Check(Field<UiTween>(button, "press").Value == 0, "returning to the capture page does not restore a stale press");

            using Bitmap image = new(200, 200);
            using (Graphics g = Graphics.FromImage(image)) { g.Clear(Color.White); g.FillRectangle(Brushes.RoyalBlue, 40, 40, 120, 120); }
            preview.Image = image;
            preview.Refresh();
            UiTween reveal = Field<UiTween>(preview, "reveal");
            Check(reveal.Value >= .82f, "new screenshot is already at least 82 percent visible in its first painted frame");
            for (int i = 0; i < 4; i++)
            {
                using Bitmap frame = new(preview.Width, preview.Height);
                preview.DrawToBitmap(frame, preview.ClientRectangle);
                frame.Save(Path.Combine(output, $"preview-{main.DeviceDpi}-{i}.png"), ImageFormat.Png);
                await Task.Delay(55);
            }
            Check(reveal.Value == 1 && !reveal.IsRunning, "preview reaches full opacity and stops repainting");
            preview.Image = null;

            UiMotion.UserEnabled = false;
            spring.SnapTo(0);
            spring.SpringTo(1);
            Check(spring.Value == 1 && !spring.IsRunning, "reduced motion gives immediate spring feedback without starting a timer");
            Call(button, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
            Check(Field<UiTween>(button, "press").Value == 1, "reduced motion retains immediate press feedback");
            Call(button, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
            Check(Field<UiTween>(button, "press").Value == 0, "reduced motion releases the press immediately");

            using UiTween hidden = new(button, () => { });
            UiMotion.UserEnabled = true;
            hidden.SpringTo(1);
            button.Hide();
            Check(hidden.Value == 1 && !hidden.IsRunning, "hidden owners finish generic tweens without running in the background");
        }
        finally
        {
            host.Close();
            UiMotion.UserEnabled = originalMotion;
        }
    }
}
