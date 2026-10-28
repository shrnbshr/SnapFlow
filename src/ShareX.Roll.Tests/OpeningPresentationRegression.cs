using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

internal static class OpeningPresentationRegression
{
    [StructLayout(LayoutKind.Sequential)] private struct WindowPosition
    {
        public nint Window, After;
        public int X, Y, Width, Height;
        public uint Flags;
    }

    private sealed class ShowProbe : NativeWindow, IDisposable
    {
        private readonly Form form;
        internal Dictionary<Control, Rectangle>? FirstBounds;
        internal Bitmap? FirstFrame;
        internal RectangleF FirstImage;
        internal ShowProbe(Form form) { this.form = form; AssignHandle(form.Handle); }
        protected override void WndProc(ref Message message)
        {
            bool showing = message.Msg == 0x47 && (Marshal.PtrToStructure<WindowPosition>(message.LParam).Flags & 0x40) != 0;
            base.WndProc(ref message);
            if (showing && FirstBounds == null)
            {
                FirstBounds = Bounds(form);
                if (form is RegionCaptureForm editor) FirstImage = editor.EditorImageBounds;
                FirstFrame = Render(form);
            }
        }
        public void Dispose() { FirstFrame?.Dispose(); ReleaseHandle(); }
    }

    private static ShowProbe? startup;
    internal static void ObserveStartup(MainForm main)
    {
        main.HandleCreated += (_, _) => startup = new(main);
        main.Disposed += (_, _) => { startup?.Dispose(); startup = null; };
    }

    private static Dictionary<Control, Rectangle> Bounds(Control root)
    {
        Dictionary<Control, Rectangle> result = [];
        void Visit(Control control)
        {
            if (!control.Visible) return;
            // The prepared settings page is behind the opaque capture workspace.
            if (root is MainForm && control is SettingsPage) return;
            Point point = root.PointToClient(control.PointToScreen(Point.Empty));
            result[control] = new(point, control.Size);
            foreach (Control child in control.Controls) Visit(child);
        }
        Visit(root);
        return result;
    }

    private static Bitmap Render(Control control)
    {
        // Draw the capture workspace directly: WM_PRINT of the whole form can
        // include the prepared settings page that is covered by this workspace.
        if (control is MainForm) control = control.Controls["workspace"]!;
        Bitmap result = new(control.Width, control.Height);
        control.DrawToBitmap(result, new Rectangle(Point.Empty, control.Size));
        return result;
    }

    private static int Verify(Form form, ShowProbe probe, string name, string output)
    {
        var after = Bounds(form);
        int changes = 0;
        foreach (var entry in probe.FirstBounds!)
        {
            if (!after.TryGetValue(entry.Key, out Rectangle bounds) || bounds != entry.Value)
            {
                changes++;
                Console.WriteLine($"MOVE: {name}: {entry.Key.GetType().Name}/{entry.Key.Name}/{entry.Key.Text}: {entry.Value} -> {bounds}");
            }
        }
        RectangleF initialImage = probe.FirstImage;
        if (form is RegionCaptureForm editor && new[] {
            Math.Abs(editor.EditorImageBounds.X - initialImage.X),
            Math.Abs(editor.EditorImageBounds.Y - initialImage.Y),
            Math.Abs(editor.EditorImageBounds.Width - initialImage.Width),
            Math.Abs(editor.EditorImageBounds.Height - initialImage.Height)
        }.Max() > .01f) // Ignore subpixel float roundoff; pixel comparison also guards visible changes.
        {
            changes++;
            Console.WriteLine($"IMAGE MOVE: {name}: {probe.FirstImage} -> {editor.EditorImageBounds}");
        }
        using Bitmap settled = Render(form);
        probe.FirstFrame!.Save(Path.Combine(output, name + "-first.png"), ImageFormat.Png);
        settled.Save(Path.Combine(output, name + "-settled.png"), ImageFormat.Png);
        int different = 0, total = 0;
        for (int y = 0; y < settled.Height; y += 2)
        for (int x = 0; x < settled.Width; x += 2)
        {
            Color first = probe.FirstFrame.GetPixel(x, y), last = settled.GetPixel(x, y);
            if (Math.Max(Math.Abs(first.R - last.R), Math.Max(Math.Abs(first.G - last.G), Math.Abs(first.B - last.B))) > 8) different++;
            total++;
        }
        Console.WriteLine($"PRESENTATION: {name}: moved={changes}, changed pixels={different}/{total}");
        return changes + (different == 0 ? 0 : 1);
    }

    internal static async Task RunAsync(MainForm main, bool baseline)
    {
        string output = Path.GetFullPath($"artifacts/opening-presentation/{(baseline ? "before" : "after")}-{main.DeviceDpi}");
        Directory.CreateDirectory(output);
        await Task.Delay(450);
        int changes = Verify(main, startup!, "main", output);
        foreach (CapturePalette palette in new[] { CapturePalette.Dark, CapturePalette.Light })
        foreach (bool transparent in new[] { false, true })
        foreach (bool portrait in new[] { false, true })
        {
            palette.ApplyLibraryTheme();
            using Bitmap source = new(960, portrait ? 3200 : 640);
            using (Graphics graphics = Graphics.FromImage(source))
            {
                graphics.Clear(Color.CornflowerBlue);
                graphics.FillRectangle(Brushes.Coral, 100, 100, 500, 300);
            }
            using RegionCaptureForm editor = ScreenshotEditor.Create(source, palette, new() { TransparencyEnabled = transparent });
            _ = editor.Handle;
            using ShowProbe probe = new(editor);
            editor.Show(main);
            await Task.Delay(450);
            string theme = palette == CapturePalette.Dark ? "dark" : "light";
            changes += Verify(editor, probe, $"editor-{theme}-{transparent}-{(portrait ? "long" : "normal")}", output);
            editor.Close();
        }
        if (!baseline && changes != 0) throw new InvalidOperationException($"Opening content moved after first presentation: {changes}");
    }
}
