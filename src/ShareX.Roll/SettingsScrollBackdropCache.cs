// GPL-3.0-or-later. A shared, idle-built backdrop for stationary settings content.
using ShareX.HelpersLib;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ShareX.Roll;

internal sealed class SettingsScrollBackdropCache : IDisposable
{
    private readonly Control content;
    private readonly Func<bool> applyingScroll, busy;
    private readonly HashSet<Control> observed = new();
    private readonly System.Windows.Forms.Timer idle = new() { Interval = 120 };
    private CapturePalette? palette;
    private Bitmap? texture, sharpTexture;
    private BackdropSnapshot? sharpMatte;
    private Rectangle textureBounds;
    private Rectangle damaged;
    private bool fullInvalidation = true;
    private int revision, readyRevision = -1;
    private long lastMovement, lastChange;
    private bool enabled, building, capturing, disposed;
    internal event EventHandler? Ready;
    internal int Builds, Hits, Discarded;
    internal bool IsReady => enabled && texture != null && readyRevision == revision;
    internal string LastChange = "initial";

    internal SettingsScrollBackdropCache(Control content, Func<bool> applyingScroll, Func<bool> busy)
    {
        this.content = content;
        this.applyingScroll = applyingScroll;
        this.busy = busy;
        Observe(content);
        if (content.Parent != null) content.Parent.LocationChanged += ViewMoved;
        idle.Tick += BuildWhenIdle;
    }

    internal void Apply(CapturePalette value, bool active)
    {
        if (palette == value && enabled == active) return;
        palette = value;
        enabled = active;
        Invalidate();
        if (!active) idle.Stop();
    }

    private void Observe(Control control)
    {
        if (!observed.Add(control)) return;
        control.Invalidated += Invalidated;
        control.LocationChanged += Moved;
        control.SizeChanged += GeometryChanged;
        control.DpiChangedAfterParent += GeometryChanged;
        control.TextChanged += Changed;
        control.FontChanged += Changed;
        control.BackColorChanged += Changed;
        control.ForeColorChanged += Changed;
        control.EnabledChanged += Changed;
        control.VisibleChanged += Changed;
        control.ControlAdded += Added;
        control.ControlRemoved += Removed;
        foreach (Control child in control.Controls) Observe(child);
    }

    private void Added(object? sender, ControlEventArgs e)
    {
        if (e.Control != null) Observe(e.Control);
        Invalidate();
    }

    private void Removed(object? sender, ControlEventArgs e)
    {
        if (e.Control != null) Forget(e.Control);
        Invalidate();
    }

    private void Invalidated(object? sender, InvalidateEventArgs e)
    {
        // Native scrolling invalidates exposed pixels without changing their content.
        // Actual value, layout, theme and tween changes still invalidate the texture.
        if (applyingScroll()) return;
        // These controls have no animated foreground. Their actual properties and
        // geometry are observed separately; repaint damage alone changes no pixels.
        if (sender is Control control)
        {
            Type type = control.GetType();
            if (control is IBackdropBackground or SettingsGroup || type == typeof(Label) ||
                type == typeof(Panel) || type == typeof(TableLayoutPanel) || type == typeof(FlowLayoutPanel)) return;
            LastChange = control.GetType().Name + ":" + control.Name + "/paint";
        }
        Invalidate(sender as Control);
    }

    private void Moved(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, content)) ViewMoved(sender, e);
        else Invalidate();
    }

    private void ViewMoved(object? sender, EventArgs e) => lastMovement = Stopwatch.GetTimestamp();

    private void Changed(object? sender, EventArgs e)
    {
        if (sender is Control control) LastChange = control.GetType().Name + ":" + control.Name + "/property";
        Invalidate(sender as Control);
    }

    private void GeometryChanged(object? sender, EventArgs e) => Invalidate();

    private void Invalidate(Control? source = null)
    {
        if (disposed || capturing) return;
        if (source == null || ReferenceEquals(source, content)) fullInvalidation = true;
        else if (!fullInvalidation)
        {
            Point origin = Point.Empty;
            Control? ancestor = source;
            while (ancestor != null && ancestor != content)
            {
                origin.Offset(ancestor.Location);
                ancestor = ancestor.Parent;
            }
            if (ancestor == null) fullInvalidation = true;
            else
            {
                Rectangle area = new(origin, source.ClientSize);
                int reach = (int)Math.Ceiling(48 * content.DeviceDpi / 96f);
                area.Inflate(reach, reach);
                damaged = damaged.IsEmpty ? area : Rectangle.Union(damaged, area);
            }
        }
        revision++;
        lastChange = Stopwatch.GetTimestamp();
        if (enabled) idle.Start();
    }

    private async void BuildWhenIdle(object? sender, EventArgs e)
    {
        if (disposed) return;
        if (!enabled || palette == null || !content.Visible || !content.IsHandleCreated) { idle.Stop(); return; }
        if (building || busy() ||
            Stopwatch.GetElapsedTime(lastMovement).TotalMilliseconds < 120 ||
            Stopwatch.GetElapsedTime(lastChange).TotalMilliseconds < 120) return;
        if (readyRevision == revision) { idle.Stop(); return; }
        float scale = content.DeviceDpi / 96f;
        int padding = (int)Math.Ceiling(48 * scale);
        Rectangle bounds = new(-padding, -padding, content.Width + padding * 2, content.Height + padding * 2);
        // Bound the sharp, blurred and coverage scenes (at most 144 MB together),
        // independently of monitor size and unusually large font settings.
        if (bounds.Width <= 0 || bounds.Height <= 0 || (long)bounds.Width * bounds.Height > 12_000_000)
        {
            idle.Stop();
            return;
        }
        int requestedRevision = revision;
        CapturePalette colors = palette;
        Bitmap? capture = null, result = null;
        building = true;
        try
        {
            capture = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
            capturing = true;
            try
            {
                using Graphics graphics = Graphics.FromImage(capture);
                graphics.Clear(colors.Background);
                nint dc = graphics.GetHdc();
                try { SettingsBarBackdrop.PrintContent(content, Point.Empty, content.ClientRectangle, bounds, dc); GdiFlush(); }
                finally { graphics.ReleaseHdc(dc); }
            }
            finally { capturing = false; }
            BackdropSnapshot coverage = BackdropSnapshot.Capture(content, capture, colors, new Point(padding, padding));
            Bitmap input = capture;
            // Only private bitmap pixels leave the UI thread; controls remain on it.
            Task<Bitmap> processing = Task.Run(() => Blur(input, scale, colors));
            result = await processing;
            if (disposed || !enabled || requestedRevision != revision)
            {
                Discarded++;
                return;
            }
            texture?.Dispose();
            sharpTexture?.Dispose();
            texture = result;
            result = null;
            sharpTexture = capture;
            sharpMatte = coverage;
            capture = null;
            textureBounds = bounds;
            readyRevision = requestedRevision;
            damaged = Rectangle.Empty;
            fullInvalidation = false;
            Builds++;
            idle.Stop();
            Ready?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is OutOfMemoryException or ExternalException or ArgumentException)
        {
            // Keep the existing per-bar rendering path available if caching fails.
            idle.Stop();
        }
        finally { capture?.Dispose(); result?.Dispose(); building = false; }
    }

    private static Bitmap Blur(Bitmap input, float scale, CapturePalette colors)
    {
        using (Bitmap small = new(Math.Max(1, (int)Math.Ceiling(input.Width / (4 * scale))),
            Math.Max(1, (int)Math.Ceiling(input.Height / (4 * scale)))))
        {
            using (Graphics graphics = Graphics.FromImage(small)) graphics.DrawImage(input, new Rectangle(Point.Empty, small.Size));
            ImageHelpers.FastBoxBlur(small, 4);
            ImageHelpers.FastBoxBlur(small, 4);
            Bitmap output = new(input.Width, input.Height, PixelFormat.Format32bppRgb);
            try
            {
                using Graphics graphics = Graphics.FromImage(output);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.CompositingMode = CompositingMode.SourceCopy;
                using ImageAttributes edges = new();
                edges.SetWrapMode(WrapMode.TileFlipXY);
                graphics.DrawImage(small, new Rectangle(Point.Empty, output.Size), 0, 0, small.Width, small.Height, GraphicsUnit.Pixel, edges);
                graphics.CompositingMode = CompositingMode.SourceOver;
                using SolidBrush tint = new(Color.FromArgb(164, colors.Background));
                graphics.FillRectangle(tint, new Rectangle(Point.Empty, output.Size));
                return output;
            }
            catch { output.Dispose(); throw; }
        }
    }

    internal bool Draw(Graphics graphics, Point origin, Size size, bool sharp = false)
    {
        if (!enabled || texture == null || fullInvalidation ||
            readyRevision != revision && damaged.IntersectsWith(new Rectangle(origin, size))) return false;
        Rectangle source = Rectangle.Intersect(new Rectangle(origin, size), textureBounds);
        graphics.Clear(palette!.Background);
        if (!source.IsEmpty)
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImage(sharp ? sharpTexture! : texture, new Rectangle(source.X - origin.X, source.Y - origin.Y, source.Width, source.Height),
                source.X - textureBounds.X, source.Y - textureBounds.Y, source.Width, source.Height, GraphicsUnit.Pixel);
        }
        Hits++;
        return true;
    }

    internal void CopySharpMatte(Point origin, Size size, Span<int> output)
    {
        output.Fill((int)sharpMatte!.Background);
        Rectangle source = Rectangle.Intersect(new Rectangle(origin, size), textureBounds);
        for (int y = source.Top; y < source.Bottom; y++)
            MemoryMarshal.Cast<uint, int>(sharpMatte.Pixels.AsSpan((y - textureBounds.Y) * textureBounds.Width + source.Left - textureBounds.X, source.Width))
                .CopyTo(output.Slice((y - origin.Y) * size.Width + source.Left - origin.X));
    }

    private void Forget(Control control)
    {
        if (!observed.Remove(control)) return;
        control.Invalidated -= Invalidated;
        control.LocationChanged -= Moved;
        control.SizeChanged -= GeometryChanged;
        control.DpiChangedAfterParent -= GeometryChanged;
        control.TextChanged -= Changed;
        control.FontChanged -= Changed;
        control.BackColorChanged -= Changed;
        control.ForeColorChanged -= Changed;
        control.EnabledChanged -= Changed;
        control.VisibleChanged -= Changed;
        control.ControlAdded -= Added;
        control.ControlRemoved -= Removed;
        foreach (Control child in control.Controls) Forget(child);
    }

    public void Dispose()
    {
        disposed = true;
        revision++;
        idle.Stop();
        idle.Dispose();
        if (content.Parent != null) content.Parent.LocationChanged -= ViewMoved;
        Forget(content);
        texture?.Dispose();
        sharpTexture?.Dispose();
        texture = null;
        sharpTexture = null;
        sharpMatte = null;
    }

    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
}
