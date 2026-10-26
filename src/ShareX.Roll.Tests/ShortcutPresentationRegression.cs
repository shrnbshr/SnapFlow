using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.Reflection;
using System.IO;

internal static class ShortcutPresentationRegression
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        checks++;
        Console.WriteLine("PASS: " + message);
    }

    private static T Find<T>(Control parent, string name) where T : Control =>
        (T)parent.Controls.Find(name, true).Single();

    private static void Command(ShortcutRecorder recorder, Keys keys) =>
        typeof(ShortcutRecorder).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(recorder, [new Message(), keys]);

    private static void Save(Control control, string path)
    {
        using Bitmap bitmap = new(control.Width, control.Height);
        control.DrawToBitmap(bitmap, control.ClientRectangle);
        bitmap.Save(path, ImageFormat.Png);
    }

    internal static void Run(bool baseline)
    {
        string output = Path.GetFullPath("artifacts/shortcut-polish");
        Directory.CreateDirectory(output);
        foreach (AppearanceMode mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
        {
            ApplicationPreferences preferences = new() { Appearance = mode, AnimationsEnabled = false };
            int saves = 0;
            using Form window = new() { Text = "SnapFlow shortcut presentation", StartPosition = FormStartPosition.CenterScreen };
            using SettingsPage page = new(preferences, new ScrollingCaptureOptions(), (_, _) => { saves++; return null; }) { Dock = DockStyle.Fill };
            window.Controls.Add(page);
            window.Show();
            float scale = page.DeviceDpi / 96f;
            window.ClientSize = new Size((int)(860 * scale), (int)(700 * scale));
            Application.DoEvents();
            Control group = Find<Control>(page, "shortcutList");
            Panel viewport = (Panel)typeof(SettingsPage).GetField("scroller", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            viewport.ScrollControlIntoView(group);
            Application.DoEvents();
            string suffix = mode + "-" + page.DeviceDpi + ".png";
            Save(group, Path.Combine(output, (baseline ? "before-" : "after-") + suffix));
            if (baseline) continue;

            ShortcutRecorder recorder = Find<ShortcutRecorder>(page, "regionShortcut");
            Button clear = Find<Button>(page, "regionClear");
            foreach (int width in new[] { 860, 680 })
            {
                window.ClientSize = new Size((int)(width * scale), (int)(700 * scale));
                Application.DoEvents();
                foreach (CaptureShortcut action in ApplicationPreferences.Actions)
                {
                    Control field = Find<Control>(page, action.Kind + "Shortcut");
                    Control remove = Find<Control>(page, action.Kind + "Clear");
                    Check(field.Height >= 52 * scale && field.Height <= 56 * scale, $"{mode}/{width}: input uses compact vertical padding ({field.Height}px, DPI {page.DeviceDpi}, font {field.Font.Size})");
                    Check(remove.Width >= 44 * scale && remove.Width == remove.Height, $"{mode}/{width}: square clear hit target");
                    Check(Math.Abs(field.Top + field.Height / 2 - remove.Top - remove.Height / 2) <= 1, $"{mode}/{width}: controls share a centerline");
                    Check(field.Parent == remove.Parent && field.Right < remove.Left && remove.Right <= field.Parent!.Width - field.Parent.Padding.Right,
                        $"{mode}/{width}: controls fit in their own card without overlap");
                }
            }
            Check(clear.AccessibilityObject.Role == AccessibleRole.PushButton && !string.IsNullOrEmpty(clear.AccessibleName), "clear retains named native button semantics");
            Keys original = recorder.Shortcut;
            recorder.Focus();
            recorder.PerformClick();
            Save(group, Path.Combine(output, "recording-" + suffix));
            Command(recorder, Keys.Escape);
            Check(recorder.Shortcut == original, "Escape cancels recording without changing the binding");
            recorder.PerformClick();
            Command(recorder, Keys.Control | Keys.Alt | Keys.Shift | Keys.PrintScreen);
            Check(recorder.Shortcut == (Keys.Control | Keys.Alt | Keys.Shift | Keys.PrintScreen), "long shortcut is accepted");
            Save(group, Path.Combine(output, "long-" + suffix));
            recorder.PerformClick();
            Command(recorder, Keys.Tab);
            Check(recorder.Shortcut == (Keys.Control | Keys.Alt | Keys.Shift | Keys.PrintScreen), "Tab leaves recording without changing the binding");
            clear.PerformClick();
            Check(recorder.Shortcut == Keys.None && !clear.Enabled, "clear unsets binding and disables redundant action");
            Save(group, Path.Combine(output, "unset-" + suffix));
            recorder.SetShortcut(original);
            Check(clear.Enabled, "setting a binding re-enables clear");
            Check(preferences.Shortcuts["region"] == original && saves == 0, "editing only changes the unsaved draft");
            recorder.Font = new Font(recorder.Font.FontFamily, recorder.Font.Size * 2);
            Application.DoEvents();
            Check(recorder.Height >= TextRenderer.MeasureText("Ag", recorder.Font).Height + (int)(8 * scale), "larger fonts grow the row without vertical clipping");
            Save(group, Path.Combine(output, "large-text-" + suffix));
            Check(recorder.AccessibleDescription?.Contains(recorder.Text) == true, "full shortcut remains available to accessibility at larger font sizes");
            window.Close();
        }
        Console.WriteLine($"Shortcut presentation: {checks} checks passed.");
    }
}
