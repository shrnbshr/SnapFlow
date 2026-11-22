// GPL-3.0-or-later. Tonal roles for the Material You style on Windows.
using System.Runtime.InteropServices;

namespace ShareX.Roll;

internal static class MaterialPalette
{
    internal const int PanelRadius = 24;
    internal const int MenuRadius = 18;
    internal const int FieldRadius = 12;
    internal const int ControlRadius = 16;
    internal const int PillRadius = 48;

    private static readonly Color DefaultAccent = Color.FromArgb(0x67, 0x50, 0xA4);
    private static readonly object Sync = new();
    private static Color seed;
    private static CapturePalette? light, dark;

    internal static CapturePalette For(bool darkMode)
    {
        Color accent = DwmGetColorizationColor(out uint argb, out _) == 0
            ? Color.FromArgb((int)(argb | 0xFF000000))
            : DefaultAccent;
        lock (Sync)
        {
            if (seed != accent)
            {
                seed = accent;
                light = dark = null;
            }
            return darkMode ? (dark ??= Create(seed, true)) : (light ??= Create(seed, false));
        }
    }

    internal static CapturePalette Create(Color accent, bool darkMode)
    {
        var (a, b) = ChromaAxes(accent);
        if (a * a + b * b < .0016) (a, b) = ChromaAxes(DefaultAccent);
        double hue = Math.Atan2(b, a);
        Color Tone(double lightness, double chroma) => ToColor(lightness, chroma, hue);
        return darkMode
            ? new CapturePalette(Tone(.195, .016), Tone(.263, .027), Tone(.215, .018), Tone(.94, .014),
                Tone(.77, .026), Tone(.82, .115), Tone(.27, .075), Tone(.37, .035), Tone(.34, .040), Tone(.40, .095))
            {
                Style = SoftwareStyle.MaterialYou, FieldBorder = Tone(.60, .048), RaisedSurface = Tone(.30, .030),
                TintText = Tone(.93, .055)
            }
            : new CapturePalette(Tone(.985, .010), Tone(.942, .026), Tone(.967, .017), Tone(.25, .022),
                Tone(.41, .037), Tone(.46, .150), Tone(.99, .007), Tone(.86, .037), Tone(.902, .038), Tone(.90, .075))
            {
                Style = SoftwareStyle.MaterialYou, FieldBorder = Tone(.55, .045), RaisedSurface = Tone(.919, .034),
                TintText = Tone(.32, .090)
            };
    }

    // OKLCH keeps the lightness of each role consistent as the Windows accent hue changes.
    // https://bottosson.github.io/posts/oklab/
    private static (double A, double B) ChromaAxes(Color color)
    {
        static double Linear(byte component)
        {
            double value = component / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        double r = Linear(color.R), g = Linear(color.G), b = Linear(color.B);
        double l = Math.Cbrt(.4122214708 * r + .5363325363 * g + .0514459929 * b);
        double m = Math.Cbrt(.2119034982 * r + .6806995451 * g + .1073969566 * b);
        double s = Math.Cbrt(.0883024619 * r + .2817188376 * g + .6299787005 * b);
        return (1.9779984951 * l - 2.4285922050 * m + .4505937099 * s,
            .0259040371 * l + .7827717662 * m - .8086757660 * s);
    }

    private static Color ToColor(double lightness, double chroma, double hue)
    {
        double x = Math.Cos(hue), y = Math.Sin(hue);
        var rgb = ToLinearRgb(lightness, chroma * x, chroma * y);
        if (!InGamut(rgb))
        {
            double low = 0, high = chroma;
            for (int i = 0; i < 16; i++)
            {
                double mid = (low + high) / 2;
                if (InGamut(ToLinearRgb(lightness, mid * x, mid * y))) low = mid;
                else high = mid;
            }
            rgb = ToLinearRgb(lightness, low * x, low * y);
        }

        static int Srgb(double value) => (int)Math.Round(255 *
            (value <= .0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1d / 2.4) - .055));
        return Color.FromArgb(Srgb(rgb.R), Srgb(rgb.G), Srgb(rgb.B));
    }

    private static bool InGamut((double R, double G, double B) rgb) =>
        rgb.R is >= 0 and <= 1 && rgb.G is >= 0 and <= 1 && rgb.B is >= 0 and <= 1;

    private static (double R, double G, double B) ToLinearRgb(double lightness, double a, double b)
    {
        static double Cube(double value) => value * value * value;
        double l = Cube(lightness + .3963377774 * a + .2158037573 * b);
        double m = Cube(lightness - .1055613458 * a - .0638541728 * b);
        double s = Cube(lightness - .0894841775 * a - 1.2914855480 * b);
        return (4.0767416621 * l - 3.3077115913 * m + .2309699292 * s,
            -1.2684380046 * l + 2.6097574011 * m - .3413193965 * s,
            -.0041960863 * l - .7034186147 * m + 1.7076147010 * s);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint colorization, out int opaqueBlend);
}
