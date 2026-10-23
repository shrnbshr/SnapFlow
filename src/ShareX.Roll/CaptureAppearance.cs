using static ShareX.Roll.Localization.UiText;
// GPL-3.0-or-later. Presentation only; native WinForms controls retain their input semantics.
using Microsoft.Win32;
using ShareX.HelpersLib;
using System.Drawing.Drawing2D;

namespace ShareX.Roll;

internal sealed record CapturePalette(Color Background, Color Surface, Color Canvas, Color Text,
    Color Secondary, Color Accent, Color AccentText, Color Border, Color Hover, Color Tint)
{
    private static Color Hex(string value) => ColorTranslator.FromHtml(value);

    internal static CapturePalette Light { get; } = new(Hex("#F5F5F7"), Hex("#FFFFFF"), Hex("#FAFAFC"),
        Hex("#1D1D1F"), Hex("#62626A"), Hex("#0066CC"), Color.White, Hex("#DEDEE4"), Hex("#ECECF1"), Hex("#E9F1FF"));
    internal static CapturePalette Dark { get; } = new(Hex("#18181B"), Hex("#242427"), Hex("#202023"),
        Hex("#F3F3F5"), Hex("#B3B3BC"), Hex("#8AB4FF"), Hex("#10213B"), Hex("#3D3D44"), Hex("#323239"), Hex("#2B3D5A"));

    internal static CapturePalette Current
    {
        get
        {
            if (SystemInformation.HighContrast)
                return new(SystemColors.Window, SystemColors.Window, SystemColors.Window, SystemColors.WindowText,
                    SystemColors.WindowText, SystemColors.Highlight, SystemColors.HighlightText,
                    SystemColors.WindowText, SystemColors.Control, SystemColors.Window);
            try
            {
                return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1) is int value && value == 0 ? Dark : Light;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return SystemColors.Window.GetBrightness() < .5 ? Dark : Light;
            }
        }
    }

    internal static CapturePalette For(AppearanceMode mode) => SystemInformation.HighContrast ? Current : mode switch
    {
        AppearanceMode.Light => Light,
        AppearanceMode.Dark => Dark,
        _ => Current
    };

    internal void ApplyLibraryTheme()
    {
        ShareXTheme theme = Surface.GetBrightness() < .5 ? ShareXTheme.DarkTheme : ShareXTheme.LightTheme;
        theme.BackgroundColor = Background;
        theme.LightBackgroundColor = Surface;
        theme.DarkBackgroundColor = Canvas;
        theme.TextColor = Text;
        theme.BorderColor = Border;
        theme.CheckerColor = Surface;
        theme.CheckerColor2 = Canvas;
        theme.LinkColor = Accent;
        theme.MenuHighlightColor = Hover;
        theme.MenuHighlightBorderColor = Accent;
        theme.MenuBorderColor = Border;
        theme.SeparatorLightColor = Surface;
        theme.SeparatorDarkColor = Border;
        ShareXResources.Theme = theme;
    }

    internal void StyleButton(Button button, bool primary = false)
    {
        bool emphasized = primary && button.Enabled;
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = emphasized ? Accent : Surface;
        button.ForeColor = emphasized ? AccentText : Text;
        button.FlatAppearance.BorderColor = emphasized ? Accent : Border;
        Color shade = AccentText.GetBrightness() > .5 ? Color.Black : Color.White;
        button.FlatAppearance.MouseOverBackColor = emphasized ? Mix(Accent, shade, .10f) : Hover;
        button.FlatAppearance.MouseDownBackColor = emphasized ? Mix(Accent, shade, .18f) : Tint;
        button.Cursor = Cursors.Hand;
        button.AccessibleName = button.Name == "btnHelp" ? L("ScrollHelp") : button.Text.Replace("&", "");
        CaptureButtonPainter.Apply(button, this, primary);
    }

    private static Color Mix(Color color, Color toward, float amount) => Color.FromArgb(
        (int)(color.R + (toward.R - color.R) * amount), (int)(color.G + (toward.G - color.G) * amount),
        (int)(color.B + (toward.B - color.B) * amount));
}

// Paint the original Button instances so their native keyboard, accessibility and click behavior stays intact.
internal static class CaptureButtonPainter
{
    private sealed class State
    {
        internal CapturePalette Palette = CapturePalette.Light;
        internal bool Primary;
        internal readonly UiTween Hover, Press;
        internal State(Button button) { Hover = new(button, button.Invalidate); Press = new(button, button.Invalidate); }
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, State> states = new();

    internal static void Apply(Button button, CapturePalette palette, bool primary)
    {
        if (!states.TryGetValue(button, out State? state))
        {
            state = new State(button);
            states.Add(button, state);
            State tracked = state;
            button.MouseEnter += (_, _) => tracked.Hover.SpringTo(1);
            button.MouseLeave += (_, _) => { tracked.Hover.SpringTo(0); tracked.Press.SpringTo(0, 90); };
            button.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) tracked.Press.SnapTo(1); };
            button.MouseUp += (_, _) => tracked.Press.SpringTo(0, 110);
            if (button.Parent is PreviewZoomBar)
                button.MouseCaptureChanged += (_, _) => { if (!button.Capture) tracked.Press.SpringTo(0, 110); };
            button.KeyDown += (_, e) => { if (e.KeyCode == Keys.Space) tracked.Press.SnapTo(1); };
            button.KeyUp += (_, _) => tracked.Press.SpringTo(0, 110);
            button.GotFocus += (_, _) => button.Invalidate();
            button.LostFocus += (_, _) => { tracked.Press.SpringTo(0); button.Invalidate(); };
            button.EnabledChanged += (_, _) =>
            {
                if (!button.Enabled) { tracked.Hover.SnapTo(0); tracked.Press.SnapTo(0); }
                button.Invalidate();
            };
            button.VisibleChanged += (_, _) =>
            {
                if (!button.Visible) { tracked.Hover.SnapTo(0); tracked.Press.SnapTo(0); }
            };
            button.Paint += (_, e) => Paint(button, tracked, e.Graphics);
        }
        state.Palette = palette;
        state.Primary = primary;
        button.Invalidate();
    }

    private static void Paint(Button button, State state, Graphics g)
    {
        CapturePalette p = state.Palette;
        float s = button.DeviceDpi / 96f;
        bool primary = state.Primary && button.Enabled;
        bool quiet = Equals(button.Tag, "quiet") && !primary;
        Color background = primary ? p.Accent : quiet ? button.Parent?.BackColor ?? p.Background : p.Surface;
        if (button.Enabled)
        {
            background = CaptureDrawing.Blend(background, button.FlatAppearance.MouseOverBackColor, state.Hover.Value);
            background = CaptureDrawing.Blend(background, button.FlatAppearance.MouseDownBackColor, state.Press.Value);
        }
        g.Clear(button.Parent?.BackColor ?? p.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float borderWidth = (button.Focused ? 1.75f : 1) * s;
        float inset = Math.Max(borderWidth / 2, .5f + state.Press.Value * .6f * s);
        using GraphicsPath path = CaptureDrawing.Rounded(new RectangleF(inset, inset, button.Width - 2 * inset, button.Height - 2 * inset), 12 * s);
        using SolidBrush fill = new(background);
        using Pen border = new(primary || button.Focused ? p.Accent : p.Border, borderWidth);
        g.FillPath(fill, path);
        if (!quiet || button.Focused) g.DrawPath(border, path);
        Color ink = primary ? p.AccentText : p.Text;
        if (!button.Enabled) ink = p.Secondary;
        if (button.Parent is PreviewZoomBar && button.Name is "previewZoomIn" or "previewZoomOut")
        {
            // Animate the glyph inside its fixed native hit target, with matching stroke weights at every DPI.
            if (button.Enabled) ink = CaptureDrawing.Blend(ink, p.Accent, Math.Max(state.Hover.Value * .55f, state.Press.Value));
            float half = 5.5f * s * (1 - .1f * state.Press.Value);
            float x = button.Width / 2f, y = button.Height / 2f;
            using Pen symbol = new(ink, 1.7f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(symbol, x - half, y, x + half, y);
            if (button.Name == "previewZoomIn") g.DrawLine(symbol, x, y - half, x, y + half);
            return;
        }
        if (button.Name is "editButton" or "copyButton" or "saveButton" or "settingsButton")
        {
            int iconSize = (int)Math.Round(16 * s);
            int gap = (int)Math.Round(5 * s);
            int available = Math.Max(0, button.Width - (int)Math.Round(12 * s));
            int textWidth = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int width = Math.Min(available, iconSize + gap + textWidth);
            int left = (button.Width - width) / 2;
            string icon = button.Name switch
            {
                "editButton" => "previewEdit", "copyButton" => "editorCopy",
                "settingsButton" => "settings", _ => "previewSave"
            };
            EditorIcons.Draw(g, new Rectangle(left, (button.Height - iconSize) / 2, iconSize, iconSize), null, icon, ink);
            TextRenderer.DrawText(g, button.Text, button.Font,
                new Rectangle(left + iconSize + gap, 0, Math.Max(0, width - iconSize - gap), button.Height), ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return;
        }
        TextRenderer.DrawText(g, button.Text, button.Font, button.ClientRectangle, ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal static class CaptureDrawing
{
    internal static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * amount), (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount));
    internal static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        GraphicsPath path = new();
        float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // One vector vocabulary: a selection frame, a window, a display, and a scrolling document.
    internal static void Icon(Graphics g, string kind, RectangleF bounds, Color color)
    {
        GraphicsState state = g.Save();
        g.TranslateTransform(bounds.X, bounds.Y);
        g.ScaleTransform(bounds.Width / 24, bounds.Height / 24);
        using Pen pen = new(color, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        if (kind is "region" or "full")
        {
            g.DrawLines(pen, [new(8, 3), new(3, 3), new(3, 8)]);
            g.DrawLines(pen, [new(16, 3), new(21, 3), new(21, 8)]);
            g.DrawLines(pen, [new(3, 16), new(3, 21), new(8, 21)]);
            g.DrawLines(pen, [new(16, 21), new(21, 21), new(21, 16)]);
            if (kind == "region")
            {
                g.DrawLine(pen, 9, 12, 15, 12);
                g.DrawLine(pen, 12, 9, 12, 15);
            }
            else g.DrawRectangle(pen, 7, 7, 10, 10);
        }
        else if (kind == "window")
        {
            using GraphicsPath path = Rounded(new RectangleF(3, 4, 18, 16), 2);
            g.DrawPath(pen, path);
            g.DrawLine(pen, 3, 9, 21, 9);
            g.DrawLine(pen, 6, 6.5f, 6.2f, 6.5f);
            g.DrawLine(pen, 9, 6.5f, 9.2f, 6.5f);
        }
        else
        {
            using GraphicsPath path = Rounded(new RectangleF(4, 2, 16, 20), 2);
            g.DrawPath(pen, path);
            g.DrawLine(pen, 8, 7, 16, 7);
            g.DrawLine(pen, 8, 11, 13, 11);
            g.DrawLine(pen, 12, 14, 12, 19);
            g.DrawLines(pen, new PointF[] { new(9.5f, 16.5f), new(12, 19), new(14.5f, 16.5f) });
        }
        g.Restore(state);
    }
}

internal sealed class CaptureModeButton : Button
{
    private readonly UiTween hover, press;
    internal string Kind { get; init; } = "region";
    internal string Shortcut { get; set; } = "";
    internal CapturePalette Palette { get; set; } = CapturePalette.Light;

    internal CaptureModeButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        hover = new(this, Invalidate);
        press = new(this, Invalidate);
    }

    protected override void OnMouseEnter(EventArgs e) { hover.SpringTo(1); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover.SpringTo(0); press.SpringTo(0); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) press.SnapTo(1); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { press.SpringTo(0, 110); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) press.SnapTo(1); base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { press.SpringTo(0, 110); base.OnKeyUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { press.SpringTo(0); Invalidate(); base.OnLostFocus(e); }
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) { hover?.SnapTo(0); press?.SnapTo(0); }
    }
    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (!Enabled) { hover?.SnapTo(0); press?.SnapTo(0); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        CapturePalette p = Palette;
        float s = DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color parent = Parent?.BackColor ?? p.Background;
        g.Clear(parent);
        float inset = 1 + press.Value * s;
        using GraphicsPath path = CaptureDrawing.Rounded(new RectangleF(inset, inset, Width - 2 * inset, Height - 2 * inset), 14 * s);
        Color background = CaptureDrawing.Blend(parent, p.Surface, hover.Value);
        background = CaptureDrawing.Blend(background, p.Hover, press.Value * .10f);
        using SolidBrush fill = new(background);
        g.FillPath(fill, path);
        if (Focused && ShowFocusCues)
        {
            using Pen focus = new(p.Accent, 1.5f * s);
            using GraphicsPath ring = CaptureDrawing.Rounded(new RectangleF(3 * s, 3 * s, Width - 6 * s, Height - 6 * s), 12 * s);
            g.DrawPath(focus, ring);
        }
        Color ink = p.Text;
        Color secondary = p.Secondary;
        float cy = Height / 2f + press.Value * .5f * s;
        Color iconInk = CaptureDrawing.Blend(p.Secondary, p.Accent, hover.Value);
        CaptureDrawing.Icon(g, Kind, new RectangleF(16 * s, cy - 11 * s, 22 * s, 22 * s), iconInk);
        using Font title = new(Font.FontFamily, 11, FontStyle.Bold);
        using Font shortcut = new("Segoe UI", 10);
        TextRenderer.DrawText(g, Text, title, new Rectangle((int)(52 * s), (int)(cy - 22 * s), Width - (int)(60 * s), (int)(26 * s)),
            ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Shortcut.Replace(" + ", "+"), shortcut, new Rectangle((int)(52 * s), (int)(cy + 3 * s), Width - (int)(60 * s), (int)(22 * s)),
            secondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed partial class PreviewCanvas : PictureBox
{
    internal CapturePalette Palette { get; set; } = CapturePalette.Light;
    private readonly UiTween reveal;
    private Image? displayed;
    private MotionFrame? revealFrame;
    private MotionFrame? revealSurface;

    internal PreviewCanvas()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
        AccessibleName = L("ImagePreview");
        reveal = new(this, Invalidate);
        InitializeViewport();
    }

    protected override void OnResize(EventArgs e)
    {
        EndReveal();
        base.OnResize(e);
        ResizeViewport();
        if (Width < 1 || Height < 1) return;
        using GraphicsPath path = CaptureDrawing.Rounded(new RectangleF(0, 0, Width, Height), 12 * DeviceDpi / 96f);
        Region? previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // A cached fade paints the complete canvas into the existing WinForms buffer.
        if (revealFrame == null) base.OnPaintBackground(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        CapturePalette p = Palette;
        float s = DeviceDpi / 96f;
        SynchronizeImage();
        if (Image != null)
        {
            if (!ReferenceEquals(displayed, Image))
            {
                EndReveal();
                displayed = Image;
                // Show the result immediately; soften only the last 18% of its appearance.
                if (UiMotion.Enabled && ClientSize.Width > 0 && ClientSize.Height > 0)
                {
                    Bitmap? frame = null;
                    try
                    {
                        frame = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                        using (Graphics target = Graphics.FromImage(frame))
                        {
                            target.Clear(BackColor);
                            // Use PictureBox's exact layout and interpolation once, not on every animation frame.
                            PaintImage(target);
                        }
                        revealFrame = new MotionFrame(frame);
                        frame = null; // Ownership has moved to the short-lived animation frame.
                        revealSurface = new MotionFrame(ClientSize);
                        reveal.SnapTo(.82f);
                        reveal.AnimateTo(1, 140);
                    }
                    catch (Exception ex) when (ex is OutOfMemoryException or System.Runtime.InteropServices.ExternalException or ArgumentException)
                    {
                        frame?.Dispose();
                        EndReveal(); // Show the original PictureBox normally if the animation copy cannot be allocated.
                    }
                }
            }
            if (revealFrame != null)
            {
                revealSurface!.Compose(g, p.Canvas, revealFrame, firstOpacity: reveal.Value);
                if (reveal.Value == 1) EndReveal();
            }
            else PaintImage(g);
            PaintViewportChrome(g);
            return;
        }
        EndReveal();
        base.OnPaint(e);
        displayed = null;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float x = Width / 2f, y = Height / 2f - 42 * s;
        bool compact = Height < 240 * s || Width < 320 * s;
        float factor = compact ? .75f : 1;
        GraphicsState state = g.Save();
        g.TranslateTransform(x, y);
        g.ScaleTransform(s * factor, s * factor);
        using SolidBrush card = new(p.Surface);
        using Pen edge = new(p.Border, 1.5f);
        using GraphicsPath back = CaptureDrawing.Rounded(new RectangleF(-37, -31, 68, 50), 10);
        g.FillPath(card, back);
        g.DrawPath(edge, back);
        using GraphicsPath front = CaptureDrawing.Rounded(new RectangleF(-27, -21, 68, 50), 10);
        using SolidBrush tint = new(p.Tint);
        g.FillPath(tint, front);
        using Pen accent = new(p.Accent, 1.6f) { LineJoin = LineJoin.Round };
        g.DrawPath(accent, front);
        using SolidBrush dot = new(p.Accent);
        g.FillEllipse(dot, -15, -9, 7, 7);
        g.DrawLines(accent, [new(-17, 16), new(-3, 3), new(8, 12), new(16, 5), new(30, 18)]);
        g.Restore(state);
        using Font title = new(Font.FontFamily, compact ? 13 : 15, FontStyle.Bold);
        using Font hint = new(Font.FontFamily, 10.5f);
        TextRenderer.DrawText(g, L("NoScreenshot"), title, new Rectangle(0, (int)(y + 54 * s), Width, (int)(32 * s)), p.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, L("ChooseCapture"), hint, new Rectangle(0, (int)(y + 90 * s), Width, (int)(28 * s)), p.Secondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void EndReveal()
    {
        if (reveal != null && (reveal.IsRunning || reveal.Value != 1)) reveal.SnapTo(1);
        revealFrame?.Dispose();
        revealFrame = null;
        revealSurface?.Dispose();
        revealSurface = null;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        if (!Visible) { EndReveal(); StopViewportMotion(); zoomBarFade?.HideImmediately(); }
        base.OnVisibleChanged(e);
        if (Visible && zoomBarFade != null) UpdateZoomBarHover();
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        EndReveal();
        base.OnBackColorChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { DisposeViewport(); reveal.Dispose(); EndReveal(); }
        base.Dispose(disposing);
    }
}
