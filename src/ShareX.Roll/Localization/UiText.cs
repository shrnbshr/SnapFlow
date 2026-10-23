using System.Globalization;

namespace ShareX.Roll.Localization;

internal static class UiText
{
    internal static string L(string key) => Strings.ResourceManager.GetString(key, UiLanguage.Culture)
        ?? throw new InvalidOperationException("Missing UI resource: " + key);
    internal static string LF(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, L(key), args);
}
