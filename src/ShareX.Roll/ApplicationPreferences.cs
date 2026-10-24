using static ShareX.Roll.Localization.UiText;
// GPL-3.0-or-later. App preferences are separate from the original scrolling options.
using Newtonsoft.Json;

namespace ShareX.Roll;

internal enum AppearanceMode { System, Light, Dark }

internal sealed record CaptureShortcut(string Kind, string TitleKey, Keys ModeKey)
{
    internal string Title => L(TitleKey);
}

internal sealed class ApplicationPreferences
{
    internal static readonly CaptureShortcut[] Actions =
    [
        new("region", "Region", Keys.A),
        new("full", "Full", Keys.F), new("scroll", "Scroll", Keys.S)
    ];

    public AppearanceMode Appearance { get; set; } = AppearanceMode.System;
    public bool AnimationsEnabled { get; set; } = true;
    public LanguageMode Language { get; set; } = LanguageMode.System;
    public bool UseDefaultSaveFolder { get; set; }
    public string DefaultSaveFolder { get; set; } = "";
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, Keys> Shortcuts { get; set; } = Actions.ToDictionary(
        action => action.Kind, action => Keys.Control | Keys.Shift | action.ModeKey);

    internal ApplicationPreferences Clone() => new()
    {
        Appearance = Appearance, AnimationsEnabled = AnimationsEnabled, Language = Language,
        UseDefaultSaveFolder = UseDefaultSaveFolder, DefaultSaveFolder = DefaultSaveFolder, Shortcuts = new(Shortcuts)
    };

    internal string? ValidateSaveFolder() => UseDefaultSaveFolder &&
        (string.IsNullOrWhiteSpace(DefaultSaveFolder) || !Path.IsPathFullyQualified(DefaultSaveFolder) || !Directory.Exists(DefaultSaveFolder))
        ? L("SaveFolderInvalid") : null;

    internal static bool IsSupported(Keys keys)
    {
        if (keys == Keys.None) return true;
        Keys code = keys & Keys.KeyCode;
        if (!Enum.IsDefined(code) || code is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu
            or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin or Keys.Escape or Keys.Tab or Keys.Return) return false;
        if ((keys & ~(Keys.KeyCode | Keys.Control | Keys.Shift | Keys.Alt)) != 0) return false;
        return (keys & (Keys.Control | Keys.Alt)) != 0 || (code >= Keys.F1 && code <= Keys.F24) || code == Keys.PrintScreen;
    }

    internal static ApplicationPreferences Load(string path)
    {
        ApplicationPreferences value = File.Exists(path)
            ? JsonConvert.DeserializeObject<ApplicationPreferences>(File.ReadAllText(path)) ?? throw new JsonException(L("EmptySettings"))
            : new();
        if (!Enum.IsDefined(value.Appearance) || value.Shortcuts == null) throw new JsonException(L("InvalidSettings"));
        value.DefaultSaveFolder ??= "";
        // Migrate retired language IDs without discarding shortcuts, theme or motion preferences.
        if ((int)value.Language is >= 4 and <= 24) value.Language = LanguageMode.System;
        else if (!Enum.IsDefined(value.Language)) throw new JsonException(L("InvalidLanguage"));
        // The merged selector uses the region binding. Keep a legacy window-only binding usable.
        if ((!value.Shortcuts.TryGetValue("region", out Keys region) || region == Keys.None)
            && value.Shortcuts.TryGetValue("window", out Keys window) && window != Keys.None && IsSupported(window))
            value.Shortcuts["region"] = window;
        value.Shortcuts.Remove("window");
        foreach (CaptureShortcut action in Actions)
        {
            if (!value.Shortcuts.TryGetValue(action.Kind, out Keys keys))
                value.Shortcuts[action.Kind] = Keys.Control | Keys.Shift | action.ModeKey;
            else if (!IsSupported(keys)) throw new JsonException(action.Title + L("InvalidShortcutSuffix"));
        }
        return value;
    }

    internal void Save(string path)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonConvert.SerializeObject(this, Formatting.Indented));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
