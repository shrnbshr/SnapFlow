// GPL-3.0-or-later. UI language selection only; regional formats and capture code are unchanged.
using Newtonsoft.Json;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ShareX.Roll;

internal enum LanguageMode
{
    System, SimplifiedChinese, TraditionalChinese, English,
    Arabic, German, Spanish, SpanishMexico, Persian, French, Hebrew, Hungarian,
    Indonesian, Italian, Japanese, Korean, Dutch, Polish, PortugueseBrazil,
    PortuguesePortugal, Romanian, Russian, Turkish, Ukrainian, Vietnamese
}

internal sealed record LanguageOption(LanguageMode Mode, string CultureName)
{
    public override string ToString() => Mode switch
    {
        LanguageMode.System => Localization.UiText.L("FollowWindows"),
        LanguageMode.SimplifiedChinese => "简体中文",
        LanguageMode.TraditionalChinese => "繁體中文",
        _ => CultureInfo.GetCultureInfo(CultureName).NativeName
    };
}

internal static class UiLanguage
{
    internal static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;
    internal static bool UsesEnglishText => Culture.TwoLetterISOLanguageName != "zh";
    internal static IReadOnlyList<LanguageOption> Options { get; } = new LanguageOption[]
    {
        new(LanguageMode.System, ""), new(LanguageMode.SimplifiedChinese, "zh-CN"), new(LanguageMode.TraditionalChinese, "zh-TW"), new(LanguageMode.English, "en"),
        new(LanguageMode.Arabic,"ar-YE"), new(LanguageMode.German,"de"), new(LanguageMode.Spanish,"es"), new(LanguageMode.SpanishMexico,"es-MX"),
        new(LanguageMode.Persian,"fa-IR"), new(LanguageMode.French,"fr"), new(LanguageMode.Hebrew,"he-IL"), new(LanguageMode.Hungarian,"hu"),
        new(LanguageMode.Indonesian,"id-ID"), new(LanguageMode.Italian,"it-IT"), new(LanguageMode.Japanese,"ja-JP"), new(LanguageMode.Korean,"ko-KR"),
        new(LanguageMode.Dutch,"nl-NL"), new(LanguageMode.Polish,"pl"), new(LanguageMode.PortugueseBrazil,"pt-BR"), new(LanguageMode.PortuguesePortugal,"pt-PT"),
        new(LanguageMode.Romanian,"ro"), new(LanguageMode.Russian,"ru"), new(LanguageMode.Turkish,"tr"), new(LanguageMode.Ukrainian,"uk"), new(LanguageMode.Vietnamese,"vi-VN")
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
            return CultureInfo.GetCultureInfo(traditional ? "zh-TW" : "zh-CN");
        }
        for (CultureInfo culture = windowsLanguage; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
        {
            LanguageOption? exact = Options.FirstOrDefault(option => option.CultureName.Equals(culture.Name, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return CultureInfo.GetCultureInfo(exact.CultureName);
        }
        if (windowsLanguage.TwoLetterISOLanguageName == "pt") return CultureInfo.GetCultureInfo("pt-PT");
        LanguageOption? related = Options.FirstOrDefault(option => option.Mode != LanguageMode.System &&
            CultureInfo.GetCultureInfo(option.CultureName).TwoLetterISOLanguageName == windowsLanguage.TwoLetterISOLanguageName);
        return CultureInfo.GetCultureInfo(related?.CultureName ?? "en");
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
