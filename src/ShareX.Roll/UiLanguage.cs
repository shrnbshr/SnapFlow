// GPL-3.0-or-later. UI language selection only; regional formats and capture code are unchanged.
using Newtonsoft.Json;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ShareX.Roll;

internal enum LanguageMode
{
    // Keep persisted values stable; value 2 now selects Hong Kong Traditional Chinese.
    System = 0, SimplifiedChinese = 1, TraditionalChinese = 2, English = 3
}

internal sealed record LanguageOption(LanguageMode Mode, string CultureName)
{
    public override string ToString() => Mode switch
    {
        LanguageMode.System => Localization.UiText.L("FollowWindows"),
        LanguageMode.SimplifiedChinese => "简体中文",
        LanguageMode.TraditionalChinese => "繁體中文（香港）",
        _ => CultureInfo.GetCultureInfo(CultureName).NativeName
    };
}

internal static class UiLanguage
{
    internal static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;
    internal static TextFormatFlags TextDirection => Culture.TextInfo.IsRightToLeft ? TextFormatFlags.RightToLeft : 0;
    internal static void ApplyTextDirection(Control root)
    {
        // Shape and order translated text without mirroring screenshot coordinates or numeric inputs.
        if (root is Label or ButtonBase or ComboBox)
            root.RightToLeft = Culture.TextInfo.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
        foreach (Control child in root.Controls) ApplyTextDirection(child);
    }
    internal static IReadOnlyList<LanguageOption> Options { get; } = new LanguageOption[]
    {
        new(LanguageMode.System, ""), new(LanguageMode.SimplifiedChinese, "zh-CN"),
        new(LanguageMode.TraditionalChinese, "zh-HK"), new(LanguageMode.English, "en")
    };

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    internal static CultureInfo Resolve(LanguageMode mode, CultureInfo windowsLanguage)
    {
        if (mode != LanguageMode.System)
            return CultureInfo.GetCultureInfo(Options.Single(option => option.Mode == mode).CultureName);
        if (windowsLanguage.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            bool traditional = windowsLanguage.Name is "zh-TW" or "zh-HK" or "zh-MO" || windowsLanguage.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase);
            return CultureInfo.GetCultureInfo(traditional ? "zh-HK" : "zh-CN");
        }
        return CultureInfo.GetCultureInfo("en");
    }

    internal static void Apply(LanguageMode mode)
    {
        CultureInfo windowsLanguage = CultureInfo.GetCultureInfo(GetUserDefaultUILanguage());
        Culture = Resolve(mode, windowsLanguage);
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
    }

    internal static void Initialize(string preferencesPath)
    {
        LanguageMode mode = LanguageMode.System;
        try { mode = ApplicationPreferences.Load(preferencesPath).Language; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // MainForm reports the read failure after the UI has been initialized.
        }
        Apply(mode);
    }
}
