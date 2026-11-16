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

    internal static void ApplyDescription(Control description, Font interfaceFont, CapturePalette palette)
    {
        string family = FamilyForDescription(interfaceFont, palette);
        Font current = description.Font;
        if (string.Equals(current.Name, family, StringComparison.OrdinalIgnoreCase)) return;

        // WinForms can replace and dispose a control's font during DPI scaling.
        // Match its current size and keep only a reference for disposal.
        DescriptionFont tracked = descriptions.GetValue(description, control =>
        {
            DescriptionFont font = new();
            control.Disposed += (_, _) => font.Assigned?.Dispose();
            return font;
        });
        Font? previous = tracked.Assigned;
        description.Font = tracked.Assigned = new Font(family, current.SizeInPoints);
        previous?.Dispose();
    }
}
