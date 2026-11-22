// GPL-3.0-or-later. Claude explanatory text with locally installed fallbacks.
using System.Runtime.CompilerServices;

namespace ShareX.Roll;

internal sealed class DescriptionLabel : Label
{
}

internal static class CaptureTypography
{
    private sealed class DescriptionFont
    {
        internal Font? Assigned { get; set; }
    }

    private static readonly ConditionalWeakTable<Control, DescriptionFont> descriptions = new();
    private static readonly Lazy<string?> latin = new(() => InstalledFamily("Lora", "Georgia", "Cambria", "Times New Roman"));
    private static readonly Lazy<string?> chinese = new(() => InstalledFamily("Source Han Serif SC", "Noto Serif CJK SC", "Noto Serif SC", "SimSun"));
    private static readonly Lazy<string?> traditional = new(() => InstalledFamily("Source Han Serif HC", "Noto Serif CJK HK", "Noto Serif HK", "PMingLiU", "SimSun"));
    private static readonly Lazy<string?> metroLatin = new(() => InstalledFamily("Segoe UI Light", "Segoe UI"));
    private static readonly Lazy<string?> metroChinese = new(() => InstalledFamily("Microsoft YaHei UI Light", "Microsoft YaHei UI"));
    private static readonly Lazy<string?> metroTraditional = new(() => InstalledFamily("Microsoft JhengHei UI Light", "Microsoft JhengHei UI"));

    private static string? InstalledFamily(params string[] names)
    {
        foreach (string name in names)
        {
            using Font candidate = new(name, 12);
            using FontFamily family = candidate.FontFamily;
            // GDI+ can report a localized name such as 宋体 for an English family request.
            if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(family.GetName(0x0409), name, StringComparison.OrdinalIgnoreCase))
                return family.Name;
        }
        return null;
    }

    private static string FamilyForDescription(Font interfaceFont, CapturePalette palette)
    {
        if (!palette.IsClaude) return interfaceFont.Name;
        Lazy<string?> family = UiLanguage.Culture.Name switch
        {
            "zh-CN" => chinese,
            "zh-HK" => traditional,
            _ => latin
        };
        return family.Value ?? interfaceFont.Name;
    }

    internal static Font CreateDescription(Font interfaceFont, float size, CapturePalette palette) =>
        new(FamilyForDescription(interfaceFont, palette), size);

    private static string DisplayFamily(Font interfaceFont) => (UiLanguage.Culture.Name switch
    {
        "zh-CN" => metroChinese,
        "zh-HK" => metroTraditional,
        _ => metroLatin
    }).Value ?? interfaceFont.Name;

    internal static Font CreateHeading(Font interfaceFont, float size, CapturePalette palette) =>
        palette.IsMetro ? new Font(DisplayFamily(interfaceFont), size + 3) : new Font(interfaceFont.FontFamily, size, FontStyle.Bold);

    internal static void ApplyHeading(Control heading, Font interfaceFont, float size, CapturePalette palette)
    {
        string family = palette.IsMetro ? DisplayFamily(interfaceFont) : interfaceFont.Name;
        float points = palette.IsMetro ? size + 3 : size;
        FontStyle weight = palette.IsMetro ? FontStyle.Regular : FontStyle.Bold;
        Font current = heading.Font;
        if (current.Name == family && current.SizeInPoints == points && current.Style == weight) return;
        AssignFont(heading, new Font(family, points, weight));
    }

    internal static void ApplyDescription(Control description, Font interfaceFont, CapturePalette palette)
    {
        string family = FamilyForDescription(interfaceFont, palette);
        Font current = description.Font;
        if (string.Equals(current.Name, family, StringComparison.OrdinalIgnoreCase)) return;

        AssignFont(description, new Font(family, current.SizeInPoints));
    }

    private static void AssignFont(Control control, Font value)
    {
        // WinForms can replace a control's current font during DPI scaling. Track only fonts we create.
        DescriptionFont tracked = descriptions.GetValue(control, owner =>
        {
            DescriptionFont font = new();
            owner.Disposed += (_, _) => font.Assigned?.Dispose();
            return font;
        });
        Font? previous = tracked.Assigned;
        control.Font = tracked.Assigned = value;
        previous?.Dispose();
    }
}
