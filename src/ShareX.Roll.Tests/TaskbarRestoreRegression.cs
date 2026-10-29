using ShareX.HelpersLib;
using ShareX.Roll;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class TaskbarRestoreRegression
{
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Call(MainForm form, string method) => typeof(MainForm).GetMethod(method, Private)!.Invoke(form, null);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private static int Cloaked(MainForm form)
    {
        int result = NativeMethods.DwmGetWindowAttribute(form.Handle, 14, out int cloak, sizeof(int));
        Check(result >= 0, "DWM cloak state is readable");
        return cloak;
    }

    [StructLayout(LayoutKind.Sequential)] private struct WindowPosition
    {
        public nint Window, After;
        public int X, Y, Width, Height;
        public uint Flags;
    }
    private sealed class PositionProbe : NativeWindow, IDisposable
    {
        internal PositionProbe(nint handle) => AssignHandle(handle);
        protected override void WndProc(ref Message message)
        {
            if (message.Msg is 0x46 or 0x47)
                Console.WriteLine($"POSITION {message.Msg:X}: flags={Marshal.PtrToStructure<WindowPosition>(message.LParam).Flags:X}");
            base.WndProc(ref message);
        }
        public void Dispose() => ReleaseHandle();
    }

    internal static async Task RunAsync(MainForm main)
    {
        FieldInfo field = typeof(MainForm).GetField("preferences", Private)!;
        ApplicationPreferences original = (ApplicationPreferences)field.GetValue(main)!;
        Size originalSize = main.Size;
        using PositionProbe probe = new(main.Handle);
        try
        {
            Call(main, "ReleaseCaptureHotkeys");
            foreach (AppearanceMode theme in new[] { AppearanceMode.Light, AppearanceMode.Dark })
            foreach (bool glass in new[] { false, true })
            foreach (bool settings in new[] { false, true })
            foreach (bool maximized in new[] { false, true })
            {
                Call(main, "CloseSettings");
                ApplicationPreferences working = original.Clone();
                working.Appearance = theme;
                working.TransparencyEnabled = glass;
                field.SetValue(main, working);
                Call(main, "ApplyAppearance");
                main.WindowState = maximized ? FormWindowState.Maximized : FormWindowState.Normal;
                if (settings) Call(main, "OpenSettings");
                await Task.Delay(350);
                string context = $"{theme}/glass={glass}/settings={settings}/maximized={maximized}";
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    Console.WriteLine($"CASE {context}/repeat={repeat}");
                    Check(Cloaked(main) == 0, "visible window starts uncloaked");
                    // Use native system commands, as the caption and Shell do, rather than
                    // the managed WindowState setter used by the older layout regression.
                    SendMessage(main.Handle, 0x112, 0xF020, 0); // SC_MINIMIZE
                    await Task.Delay(180);
                    Check(IsIconic(main.Handle), "native minimize reaches the iconic state");
                    Check((Cloaked(main) & 1) == 0, "minimized window is not left application-cloaked");
                    SendMessage(main.Handle, 0x112, 0xF120, 0); // SC_RESTORE
                    await Task.Delay(300);
                    Check(!IsIconic(main.Handle) && main.Visible && Cloaked(main) == 0,
                        "native restore makes the window visible and uncloaked");
                    Check(main.WindowState == (maximized ? FormWindowState.Maximized : FormWindowState.Normal),
                        "native restore preserves the previous normal/maximized state");
                }
                main.Hide();
                main.Show();
                await Task.Delay(150);
                Check(main.Visible && Cloaked(main) == 0, "capture-style hide/show releases the first-frame cloak");
            }
        }
        finally
        {
            // Also recover the baseline window when an assertion catches a leaked cloak.
            int cloak = 0;
            NativeMethods.DwmSetWindowAttribute(main.Handle, 13, ref cloak, sizeof(int));
            main.WindowState = FormWindowState.Normal;
            Call(main, "CloseSettings");
            field.SetValue(main, original);
            main.Size = originalSize;
            Call(main, "ApplyAppearance");
            Call(main, "RegisterCaptureHotkeys");
        }
    }
}
