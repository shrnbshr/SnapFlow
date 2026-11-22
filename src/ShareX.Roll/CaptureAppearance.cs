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
        Hex("#1D1D1F"), Hex("#484850"), Hex("#0066CC"), Color.White, Hex("#DEDEE4"), Hex("#ECECF1"), Hex("#E9F1FF"))
        { LinkInk = Hex("#00488F") };
    internal static CapturePalette Dark { get; } = new(Hex("#18181B"), Hex("#242427"), Hex("#202023"),
        Hex("#F3F3F5"), Hex("#D8D8DE"), Hex("#8AB4FF"), Hex("#10213B"), Hex("#3D3D44"), Hex("#323239"), Hex("#2B3D5A"))
        { LinkInk = Hex("#A9CEFF") };

    internal static CapturePalette ClaudeLight { get; } = new(Hex("#FAF9F5"), Hex("#F1EEE5"), Hex("#FAF9F5"),
        Hex("#141413"), Hex("#625F56"), Hex("#99452F"), Hex("#FAF9F5"), Hex("#E1DDD2"), Hex("#E8E6DC"), Hex("#F4E3D8"))
        { Style = SoftwareStyle.Claude, IllustrationAccent = Hex("#D97757"), FieldBorder = Hex("#837E71") };
    internal static CapturePalette ClaudeDark { get; } = new(Hex("#1C1B19"), Hex("#2A2723"), Hex("#1C1B19"),
        Hex("#FAF9F5"), Hex("#B0AEA5"), Hex("#F3A48A"), Hex("#1C1B19"), Hex("#4D453C"), Hex("#38322B"), Hex("#453028"))
        { Style = SoftwareStyle.Claude, IllustrationAccent = Hex("#F3A48A"), FieldBorder = Hex("#887E71") };

    internal static CapturePalette WinUILight { get; } = new(Hex("#F3F3F3"), Hex("#FBFBFB"), Hex("#FFFFFF"),
        Hex("#1A1A1A"), Hex("#484848"), Hex("#005FB8"), Color.White, Hex("#E0E0E0"), Hex("#EEEEEE"), Hex("#E5EFF9"))
        { Style = SoftwareStyle.WinUI, FieldBorder = Hex("#858585"), RaisedSurface = Hex("#FFFFFF"), TintText = Hex("#003760"), LinkInk = Hex("#004E97") };
    internal static CapturePalette WinUIDark { get; } = new(Hex("#202020"), Hex("#2B2B2B"), Hex("#272727"),
        Hex("#FFFFFF"), Hex("#DEDEDE"), Hex("#60CDFF"), Hex("#001926"), Hex("#424242"), Hex("#383838"), Hex("#244351"))
        { Style = SoftwareStyle.WinUI, FieldBorder = Hex("#959595"), RaisedSurface = Hex("#323232"), TintText = Hex("#CDEEFF") };

    internal static CapturePalette MetroLight { get; } = new(Hex("#F7F7F7"), Color.White, Hex("#EDEDED"),
        Hex("#171717"), Hex("#505050"), Hex("#5932A5"), Color.White, Hex("#D1D1D1"), Hex("#E4DDED"), Hex("#E8DEF9"))
        { Style = SoftwareStyle.MetroClassic, FieldBorder = Hex("#767676"), RaisedSurface = Hex("#ECECEC"), TintText = Hex("#3E1D77") };
    internal static CapturePalette MetroDark { get; } = new(Hex("#141414"), Hex("#1F1F1F"), Hex("#0E0E0E"),
        Hex("#FFFFFF"), Hex("#C4C4C4"), Hex("#C5A7FF"), Hex("#1A0B2B"), Hex("#454545"), Hex("#36303F"), Hex("#432965"))
        { Style = SoftwareStyle.MetroClassic, FieldBorder = Hex("#929292"), RaisedSurface = Hex("#2B2B2B"), TintText = Hex("#EBDDFF") };

    internal const int WinUIControlRadius = 4;
    internal const int WinUIOverlayRadius = 8;
    internal SoftwareStyle Style { get; init; }
    internal bool IsClaude => Style == SoftwareStyle.Claude;
    internal bool IsMaterial => Style == SoftwareStyle.MaterialYou;
    internal bool IsWinUI => Style == SoftwareStyle.WinUI;
    internal bool IsMetro => Style == SoftwareStyle.MetroClassic;
    internal bool HasShapedControls => Style != SoftwareStyle.Default;
    internal bool UsesFilledActions => IsMaterial || IsWinUI || IsMetro;
    internal Color IllustrationAccent { get; init; }
    internal Color FieldBorder { get; init; }
    internal Color RaisedSurface { get; init; }
    internal Color TintText { get; init; }
    internal Color LinkInk { get; init; } = Accent;
    internal bool SupportsBackdrop => Style is SoftwareStyle.Default or SoftwareStyle.WinUI;
    internal bool AllowsTransparency(bool enabled) => enabled && SupportsBackdrop;
    internal float CornerRadius(float radius) => IsMetro ? 0 : radius;
    internal int MenuRadius => IsMetro ? 0 : IsWinUI ? WinUIOverlayRadius : IsMaterial ? MaterialPalette.MenuRadius : 10;
    internal Color MenuSurface => IsMaterial ? RaisedSurface : Background;
    internal Color PrimaryFill(float hover, float press) =>
        CaptureDrawing.Blend(Accent, AccentText, Math.Max(.08f * hover, .14f * press));
    internal Color WinUIHoverFill(Color resting, float amount = 1) =>
        CaptureDrawing.Blend(resting, Text, .10f * amount);

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

    internal static CapturePalette For(AppearanceMode mode, SoftwareStyle style = SoftwareStyle.Default)
    {
        if (SystemInformation.HighContrast) return Current;
        bool dark = mode switch
        {
            AppearanceMode.Light => false,
            AppearanceMode.Dark => true,
            _ => Current == Dark
        };
        return style switch
        {
            SoftwareStyle.Claude => dark ? ClaudeDark : ClaudeLight,
            SoftwareStyle.MaterialYou => MaterialPalette.For(dark),
            SoftwareStyle.WinUI => dark ? WinUIDark : WinUILight,
            SoftwareStyle.MetroClassic => dark ? MetroDark : MetroLight,
            _ => dark ? Dark : Light
        };
    }

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
        theme.LinkColor = LinkInk;
        theme.MenuHighlightColor = IsMaterial || IsMetro ? Tint : Hover;
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
        button.FlatAppearance.BorderSize = 0;
        button.UseVisualStyleBackColor = false;
        button.BackColor = emphasized && !IsClaude ? Accent : Surface;
        button.ForeColor = emphasized ? (IsClaude ? Accent : AccentText) : Text;
        button.FlatAppearance.BorderColor = emphasized ? Accent : Border;
        Color shade = AccentText.GetBrightness() > .5 ? Color.Black : Color.White;
        button.FlatAppearance.MouseOverBackColor = emphasized ? Mix(Accent, shade, .10f) :
            button.Parent is PreviewZoomBar ? Hover : ButtonElevation.Surface(this);
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
    internal static int PreferredActionWidth(Button button)
    {
        float scale = button.DeviceDpi / 96f;
        int text = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        // Two shadow gutters, face padding, icon, icon-label gap and a small GDI rounding allowance.
        return text + (int)Math.Ceiling((32 + 2 * ButtonSpacing.Horizontal + 16 + ButtonSpacing.IconGap + 2) * scale);
    }

    private sealed class State
    {
        internal CapturePalette Palette = CapturePalette.Light;
        internal bool Primary;
        internal readonly UiTween Hover, Press;
        internal readonly ButtonElevation Elevation = new();
        internal State(Button button)
        {
            Hover = new(button, button.Invalidate);
            Press = new(button, button.Invalidate);
            bool contentSized = button.AutoSize;
            if (contentSized && button.Parent is not PreviewZoomBar) button.AutoSize = false;
            void SizeToContent()
            {
                if (!button.IsHandleCreated || button.Parent is PreviewZoomBar) return;
                float scale = button.DeviceDpi / 96f;
                bool icon = button.Name is "editButton" or "copyButton" or "saveButton" or "settingsButton";
                Size text = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int width = icon ? PreferredActionWidth(button) : text.Width + (int)Math.Ceiling((32 + 2 * ButtonSpacing.Horizontal + 2) * scale);
                int height = Math.Max((int)Math.Ceiling(ButtonSpacing.ActionHeight * scale), text.Height + (int)Math.Ceiling((24 + 2 * ButtonSpacing.Vertical) * scale));
                // These are device-pixel measurements. WinForms may already have scaled the
                // original MinimumSize before the painter is attached (including hidden controls).
                button.MinimumSize = new Size(width, height);
                if (contentSized) button.Size = button.MinimumSize;
            }
            button.HandleCreated += (_, _) => SizeToContent();
            button.FontChanged += (_, _) => SizeToContent();
            button.TextChanged += (_, _) => SizeToContent();
            button.DpiChangedAfterParent += (_, _) => SizeToContent();
            SizeToContent();
            button.Disposed += (_, _) => Elevation.Dispose();
        }
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, State> states = new();

    internal static bool HasBackdrop(Control control) => control is CaptureModeButton or ThemeOption || control is Button button && states.TryGetValue(button, out _);

    internal static void PaintBackdrop(Control control, Graphics graphics)
    {
        if (control is CaptureModeButton mode) mode.PaintBackdrop(graphics);
        else if (control is ThemeOption theme) theme.PaintBackdrop(graphics);
        else if (control is Button button && states.TryGetValue(button, out State? state)) Paint(button, state, graphics, matte: true);
    }

    internal static Color Coverage(float amount)
    {
        int value = Math.Clamp((int)Math.Round(amount * 255), 0, 255);
        return Color.FromArgb(value, value, value);
    }

    internal static void Apply(Button button, CapturePalette palette, bool primary)
    {
        if (!states.TryGetValue(button, out State? state))
        {
            state = new State(button);
            states.Add(button, state);
            State tracked = state;
            button.MouseEnter += (_, _) => tracked.Hover.SpringTo(1, ButtonElevation.ResponseMilliseconds);
            button.MouseLeave += (_, _) =>
            {
                tracked.Hover.SpringTo(0, ButtonElevation.ResponseMilliseconds);
                tracked.Press.SpringTo(0, 90);
            };
            button.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) tracked.Press.SnapTo(1); };
            button.MouseUp += (_, _) => tracked.Press.SpringTo(0, 110);
            button.MouseCaptureChanged += (_, _) => { if (!button.Capture) tracked.Press.SpringTo(0, 110); };
            button.KeyDown += (_, e) => { if (e.KeyCode == Keys.Space) tracked.Press.SnapTo(1); };
            button.KeyUp += (_, _) => tracked.Press.SpringTo(0, 110);
            button.GotFocus += (_, _) => button.Invalidate();
            button.ChangeUICues += (_, _) => button.Invalidate();
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
        float radius = palette.CornerRadius(palette.IsWinUI ? CapturePalette.WinUIControlRadius :
            palette.IsMaterial ? MaterialPalette.PillRadius : 8);
        if (button.Parent is PreviewZoomBar) ButtonPointerBounds.ApplyInset(button, palette.HasShapedControls, radius);
        else ButtonPointerBounds.Apply(button, palette.HasShapedControls, radius: radius);
        button.Invalidate();
    }

    private static void Paint(Button button, State state, Graphics g, bool matte = false)
    {
        CapturePalette p = state.Palette;
        float s = button.DeviceDpi / 96f;
        bool primary = state.Primary && button.Enabled;
        bool quiet = Equals(button.Tag, "quiet") && !primary;
        bool textAction = p.IsClaude && (state.Primary || quiet &&
            (button.Parent is not PreviewZoomBar || button.Name == "previewFitWidth"));
        Color background = textAction || quiet ? button.Parent?.BackColor ?? p.Background :
            primary ? p.Accent : p.UsesFilledActions ? p.RaisedSurface : p.Surface;
        if (primary && p.UsesFilledActions)
        {
            background = p.PrimaryFill(state.Hover.Value, state.Press.Value);
        }
        else if (button.Enabled && !textAction)
        {
            background = CaptureDrawing.Blend(background, button.FlatAppearance.MouseOverBackColor, state.Hover.Value);
            background = CaptureDrawing.Blend(background, button.FlatAppearance.MouseDownBackColor, state.Press.Value);
        }
        bool glassParent = (button.Parent?.BackColor ?? p.Background) == p.Background;
        if (matte) background = Coverage(quiet && glassParent ? (1 - state.Hover.Value) * (1 - state.Press.Value) : 0);
        PreviewZoomBar? zoomBar = !matte ? button.Parent as PreviewZoomBar : null;
        if (zoomBar != null)
        {
            zoomBar.PaintSurface(g, button.Location);
            // Keep labels and glyphs opaque; blend only the hover/pressed feedback over the material.
            if (quiet || textAction)
                background = Color.FromArgb(textAction ? 0 :
                    (int)Math.Round(255 * (1 - (1 - state.Hover.Value) * (1 - state.Press.Value))), background);
        }
        else g.Clear(matte ? (glassParent ? Color.White : Color.Black) : button.Parent?.BackColor ?? p.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool focus = button.Focused && button.IsHandleCreated &&
            (NativeMethods.SendMessage(button.Handle, (uint)WindowsMessages.QUERYUISTATE, IntPtr.Zero, IntPtr.Zero).ToInt64() & 1) == 0;
        float borderWidth = (focus ? 1.75f : 1) * s;
        bool material = button.Parent is not PreviewZoomBar;
        float elevation = button.Enabled && button.Parent is not PreviewZoomBar && !SystemInformation.HighContrast && p.Style == SoftwareStyle.Default
            ? state.Hover.Value * (1 - state.Press.Value) : 0;
        float lift = UiMotion.Enabled ? elevation * ButtonElevation.Lift * s : 0;
        RectangleF face = material ? ButtonElevation.FaceBounds(button.ClientSize, s) :
            new RectangleF(s, s, button.Width - 2 * s, button.Height - 2 * s);
        if (material && p.Style == SoftwareStyle.Default)
        {
            face.Offset(0, -lift);
            face.Inflate(-state.Press.Value * .5f * s, -state.Press.Value * .5f * s);
        }
        state.Elevation.PaintShadow(g, button.ClientSize, s, p.Background.GetBrightness() < .5f, elevation, unified: true);
        float radius = p.IsWinUI ? CapturePalette.WinUIControlRadius * s :
            p.IsMaterial ? face.Height / 2 : p.IsClaude ? 8 * s : material ? ButtonElevation.Radius(face, s) : 12 * s;
        radius = p.CornerRadius(radius);
        if (p.IsMaterial || p.IsWinUI) CaptureDrawing.InsetAntialiasedFace(ref face, ref radius);
        using GraphicsPath path = CaptureDrawing.Rounded(face, radius);
        using (SolidBrush fill = new(background)) g.FillPath(fill, path);
        if (focus || SystemInformation.HighContrast || p.IsClaude && !quiet && !textAction || p.IsWinUI && !quiet && !primary)
        {
            Color edge = matte ? Color.Black : focus ? p.Accent : p.IsClaude && button.Enabled
                ? CaptureDrawing.Blend(p.Border, p.FieldBorder, state.Hover.Value) : p.Border;
            if (p.HasShapedControls)
                CaptureDrawing.DrawInsideBorder(g, face, radius, p.UsesFilledActions && primary && focus ? p.AccentText : edge, borderWidth);
            else
            {
                using Pen border = new(edge, borderWidth);
                g.DrawPath(border, path);
            }
        }
        Color ink = primary ? p.AccentText : p.Text;
        if (!button.Enabled) ink = p.Secondary;
        if (matte) ink = Color.Black;
        float emphasis = button.Enabled ? Math.Max(state.Hover.Value, state.Press.Value) : 0;
        if (textAction)
        {
            Rectangle caption = Rectangle.Round(face);
            caption.Inflate(-(int)Math.Round(ButtonSpacing.Horizontal * s), 0);
            Color textInk = matte ? ink : primary ? p.Accent : CaptureDrawing.Blend(ink, p.Accent, emphasis);
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | UiLanguage.TextDirection;
            Color lineInk = matte || primary ? textInk : CaptureDrawing.Blend(p.Secondary, p.Accent, emphasis);
            if (button.Enabled) CaptureDrawing.DrawUnderlinedText(g, button.Text, button.Font, caption, textInk, lineInk, flags, s, emphasis);
            else TextRenderer.DrawText(g, button.Text, button.Font, caption, textInk, flags);
        }
        else PaintContent(button, state, g, ink, matte);
    }

    private static void PaintContent(Button button, State state, Graphics g, Color ink, bool matte)
    {
        float s = button.DeviceDpi / 96f;
        CapturePalette p = state.Palette;
        if (button.Parent is PreviewZoomBar && button.Name is "previewZoomIn" or "previewZoomOut")
        {
            // Animate the glyph inside its fixed native hit target, with matching stroke weights at every DPI.
            if (!matte && button.Enabled) ink = CaptureDrawing.Blend(ink, p.Accent, Math.Max(state.Hover.Value * .55f, state.Press.Value));
            float half = 5.5f * s * (1 - .1f * state.Press.Value);
            float x = button.Width / 2f, y = button.Height / 2f;
            if (ThemeIcons.TryDraw(g, ThemeIcons.Centered(x, y, 3 * half), p.Style,
                button.Name == "previewZoomIn" ? "plus" : "minus", ink)) return;
            using Pen symbol = new(ink, 1.7f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(symbol, x - half, y, x + half, y);
            if (button.Name == "previewZoomIn") g.DrawLine(symbol, x, y - half, x, y + half);
            return;
        }
        if (button.Name is "editButton" or "copyButton" or "saveButton" or "settingsButton")
        {
            int iconSize = (int)Math.Round(16 * s);
            int gap = (int)Math.Round(ButtonSpacing.IconGap * s);
            RectangleF face = ButtonElevation.FaceBounds(button.ClientSize, s);
            int available = Math.Max(0, (int)face.Width - (int)Math.Round(2 * ButtonSpacing.Horizontal * s));
            int textWidth = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int width = Math.Min(available, iconSize + gap + textWidth);
            int left = (button.Width - width) / 2;
            string icon = button.Name switch
            {
                "editButton" => "previewEdit", "copyButton" => "editorCopy",
                "settingsButton" => "settings", _ => "previewSave"
            };
            EditorIcons.Draw(g, new Rectangle(left, (button.Height - iconSize) / 2, iconSize, iconSize), null, icon, ink, p.Style);
            TextRenderer.DrawText(g, button.Text, button.Font,
                new Rectangle(left + iconSize + gap, 0, Math.Max(0, width - iconSize - gap), button.Height), ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | UiLanguage.TextDirection);
            return;
        }
        Rectangle text = button.Parent is PreviewZoomBar ? button.ClientRectangle :
            Rectangle.Round(ButtonElevation.FaceBounds(button.ClientSize, s));
        if (button.Parent is not PreviewZoomBar) text.Inflate(-(int)(ButtonSpacing.Horizontal * s), 0);
        TextRenderer.DrawText(g, button.Text, button.Font, text, ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | UiLanguage.TextDirection);
    }
}

internal static class CaptureDrawing
{
    internal static void DrawUnderlinedText(Graphics graphics, string? text, Font font, Rectangle bounds, Color ink, Color lineInk,
        TextFormatFlags flags, float scale, float emphasis)
    {
        if (string.IsNullOrEmpty(text) || bounds.Width <= 0 || bounds.Height <= 0) return;
        TextRenderer.DrawText(graphics, text, font, bounds, ink, flags);
        Size size = TextRenderer.MeasureText(graphics, text, font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | (flags & TextFormatFlags.NoPrefix));
        int width = Math.Min(bounds.Width, size.Width);
        int left = (flags & TextFormatFlags.HorizontalCenter) != 0 ? bounds.Left + (bounds.Width - width) / 2 :
            (flags & TextFormatFlags.Right) != 0 ? bounds.Right - width : bounds.Left;
        int thickest = Math.Max(1, (int)MathF.Round(2 * scale));
        int thickness = Math.Max(1, (int)MathF.Round((1 + emphasis) * scale));
        int top = (flags & TextFormatFlags.VerticalCenter) != 0 ? bounds.Top + (bounds.Height - size.Height) / 2 : bounds.Top;
        int gap = Math.Max(1, (int)MathF.Round(scale));
        int y = Math.Clamp(top + size.Height + gap, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - thickest));
        using SolidBrush line = new(lineInk);
        // Fill whole pixel rows so high-DPI antialiasing does not soften the line ends.
        GraphicsState state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.FillRectangle(line, left, y, width, Math.Min(thickness, bounds.Bottom - y));
        graphics.Restore(state);
    }

    internal static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * amount), (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount));
    internal static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        GraphicsPath path = new();
        float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0)
        {
            if (rect.Width > 0 && rect.Height > 0) path.AddRectangle(rect);
            return path;
        }
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static int BorderPixelWidth(float thickness) =>
        Math.Max(1, (int)MathF.Round(thickness, MidpointRounding.AwayFromZero));

    internal static RectangleF PixelAlignedBounds(RectangleF bounds) =>
        RectangleF.FromLTRB(MathF.Ceiling(bounds.Left), MathF.Ceiling(bounds.Top),
            MathF.Floor(bounds.Right), MathF.Floor(bounds.Bottom));

    internal static void InsetAntialiasedFace(ref RectangleF bounds, ref float radius)
    {
        // The native hit region has a hard edge. Leave room for two device pixels between it
        // and the painted contour so it cannot cut off the curve's antialiasing.
        const float clearance = 2;
        bounds.Inflate(-clearance, -clearance);
        if (radius > 0) radius = Math.Max(1, radius - clearance);
    }

    internal static void DrawInsideBorder(Graphics graphics, RectangleF bounds, float radius, Color color, float thickness)
    {
        // A centered pen straddles the HWND region and its surviving half can
        // rasterize differently on opposing edges. Fill a pixel-aligned ring inside it.
        RectangleF outer = PixelAlignedBounds(bounds);
        if (outer.Width <= 0 || outer.Height <= 0) return;
        int width = BorderPixelWidth(thickness);
        using GraphicsPath ring = radius > 0 ? Rounded(outer, radius) : new GraphicsPath();
        if (radius <= 0) ring.AddRectangle(outer);
        ring.FillMode = FillMode.Alternate;
        RectangleF inner = outer;
        inner.Inflate(-width, -width);
        if (inner.Width > 0 && inner.Height > 0)
        {
            float innerRadius = Math.Min(radius, Math.Min(outer.Width, outer.Height) / 2) - width;
            if (innerRadius > 0)
            {
                using GraphicsPath inset = Rounded(inner, innerRadius);
                ring.AddPath(inset, false);
            }
            else ring.AddRectangle(inner);
        }
        using SolidBrush brush = new(color);
        PixelOffsetMode previous = graphics.PixelOffsetMode;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.FillPath(brush, ring);
        graphics.PixelOffsetMode = previous;
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
    private static readonly Color RegionTile = Color.FromArgb(0, 90, 158);
    private static readonly Color FullTile = Color.FromArgb(0, 112, 115);
    private static readonly Color ScrollTile = Color.FromArgb(97, 50, 166);
    private readonly UiTween hover, press;
    private CapturePalette palette = CapturePalette.Light;
    internal string Kind { get; init; } = "region";
    internal string Shortcut { get; set; } = "";
    internal CapturePalette Palette
    {
        get => palette;
        set
        {
            palette = value;
            ButtonPointerBounds.ApplyInset(this, true, CommandRadius(value));
            Invalidate();
        }
    }

    private static float CommandRadius(CapturePalette palette) => palette.CornerRadius(palette.IsWinUI ? CapturePalette.WinUIControlRadius : palette.IsMaterial ? MaterialPalette.ControlRadius : 9);

    internal CaptureModeButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Size = new Size(44, 44);
        Margin = new Padding(0, 0, 4, 0);
        hover = new(this, Invalidate);
        press = new(this, Invalidate);
    }

    protected override void OnMouseEnter(EventArgs e) { hover.SpringTo(1); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover.SpringTo(0); press.SpringTo(0); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) press.SnapTo(1); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { press.SpringTo(0, 110); base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) press.SpringTo(0, 110); base.OnMouseCaptureChanged(e); }
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
        Render(e.Graphics, matte: false);
    }

    internal void PaintBackdrop(Graphics graphics) => Render(graphics, matte: true);

    private void Render(Graphics g, bool matte)
    {
        CapturePalette p = Palette;
        float s = DeviceDpi / 96f;
        Color parent = Parent?.BackColor ?? p.Background;
        bool glassParent = parent == p.Background;
        g.Clear(matte ? (glassParent ? Color.White : Color.Black) : parent);
        if (Width <= 4 * s || Height <= 4 * s) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF face = new(s, s, Width - 2 * s, Height - 2 * s);
        float radius = CommandRadius(p) * s;
        CaptureDrawing.InsetAntialiasedFace(ref face, ref radius);
        bool focused = Focused && ShowFocusCues;
        float hoverAmount = Enabled ? hover.Value : 0;
        float pressAmount = Enabled ? press.Value : 0;
        Color fill = CaptureDrawing.Blend(CaptureDrawing.Blend(parent, p.Hover, hoverAmount), p.Tint, pressAmount);
        Color tile = p.IsMetro && Enabled ? Kind switch
        {
            "region" => RegionTile,
            "full" => FullTile,
            "scroll" => ScrollTile,
            _ => Color.Empty
        } : Color.Empty;
        if (!tile.IsEmpty)
            fill = CaptureDrawing.Blend(tile, Color.Black, Math.Max(hoverAmount * .14f, pressAmount * .26f));
        using (GraphicsPath path = CaptureDrawing.Rounded(face, radius))
        {
            float coverage = glassParent ? (1 - hoverAmount) * (1 - pressAmount) : 0;
            using SolidBrush brush = new(matte ? CaptureButtonPainter.Coverage(coverage) : fill);
            g.FillPath(brush, path);
        }
        if (focused)
            CaptureDrawing.DrawInsideBorder(g, face, radius, matte ? Color.Black : tile.IsEmpty ? p.Accent : Color.White, 1.5f * s);
        Color ink = matte ? Color.Black : !tile.IsEmpty ? Color.White : !Enabled ? p.Secondary :
            CaptureDrawing.Blend(p.Text, p.IsMaterial ? p.TintText : p.Accent, Math.Max(hoverAmount, pressAmount));
        int size = (int)Math.Round(20 * s);
        Rectangle icon = new((Width - size) / 2, (Height - size) / 2, size, size);
        if (Kind is "region" or "full" or "scroll")
        {
            if (!ThemeIcons.TryDraw(g, icon, p.Style, Kind, ink)) CaptureDrawing.Icon(g, Kind, icon, ink);
        }
        else
            EditorIcons.Draw(g, icon, null, Kind, ink, p.Style);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { hover.Dispose(); press.Dispose(); }
        base.Dispose(disposing);
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
        viewportTimer = new ScrollFrameClock(this, AdvanceViewport);
        InitializeViewport();
    }

    protected override void OnResize(EventArgs e)
    {
        EndReveal();
        base.OnResize(e);
        ResizeViewport();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // A cached fade paints the complete canvas into the existing WinForms buffer.
        if (revealFrame != null) return;
        base.OnPaintBackground(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        CapturePalette p = Palette;
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
                        reveal.AnimateTo(1, p.IsClaude ? 220 : 140);
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
            PaintZoomControlsInCurrentFrame();
            return;
        }
        EndReveal();
        base.OnPaint(e);
        displayed = null;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using Font hint = p.IsMetro ? CaptureTypography.CreateHeading(Font, 15, p) : CaptureTypography.CreateDescription(Font, 11, p);
        TextRenderer.DrawText(g, L("ChooseCapture"), hint, ClientRectangle, p.Secondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | UiLanguage.TextDirection);
        PaintViewportChrome(g);
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
