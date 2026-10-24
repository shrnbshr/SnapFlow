using static ShareX.Roll.Localization.UiText;
// GPL-3.0-or-later. Settings and shortcut routing only; capture implementations stay unchanged.
using Newtonsoft.Json;
using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;

namespace ShareX.Roll;

public sealed partial class MainForm
{
    private readonly Button settingsButton = new() { Name = "settingsButton", Text = L("Settings"), AutoSize = true, MinimumSize = new Size(88, 38) };
    private readonly string preferencesPath = Path.Combine(AppContext.BaseDirectory, "SnapFlow.preferences.json");
    private ApplicationPreferences preferences = new();
    private readonly Dictionary<ushort, string> hotkeyActions = new();
    private SettingsPage? settingsPage;
    private string? preferencesWarning;

    private void LoadPreferences()
    {
        try { preferences = ApplicationPreferences.Load(preferencesPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            preferencesWarning = L("PreferencesReadFailed") + ex.Message;
        }
    }

    private void ReleaseCaptureHotkeys()
    {
        foreach (HotkeyInfo key in hotkeys.Where(key => key.Status == HotkeyStatus.Registered)) UnregisterHotkey(key);
        hotkeys.Clear();
        hotkeyActions.Clear();
    }

    private void RegisterCaptureHotkeys()
    {
        ReleaseCaptureHotkeys();
        List<string> conflicts = new();
        foreach (CaptureShortcut action in ApplicationPreferences.Actions)
        {
            HotkeyInfo hotkey = new(preferences.Shortcuts[action.Kind]);
            RegisterHotkey(hotkey);
            // Preserve the existing fallback for the default scrolling shortcut only.
            if (action.Kind == "scroll" && hotkey.Hotkey == (Keys.Control | Keys.Shift | Keys.S)
                && hotkey.Status != HotkeyStatus.Registered)
            {
                hotkey = new(Keys.Control | Keys.Alt | Keys.S);
                RegisterHotkey(hotkey);
            }
            hotkeys.Add(hotkey);
            if (hotkey.Status == HotkeyStatus.Registered) hotkeyActions[hotkey.ID] = action.Kind;
            else if (hotkey.Hotkey != Keys.None) conflicts.Add(action.Title);
            UpdateShortcutLabel(action.ModeKey, hotkey);
        }
        status.Text = preferencesWarning ?? (conflicts.Count == 0
            ? L("StartHint")
            : string.Join(L("ListSeparator"), conflicts) + L("ShortcutConflictSuffix"));
    }

    private async Task DispatchCaptureHotkeyAsync(ushort id)
    {
        if (settingsPage != null || !hotkeyActions.TryGetValue(id, out string? kind)) return;
        await CaptureAsync(kind);
    }

    private void OpenSettings()
    {
        if (settingsPage != null) { settingsPage.Focus(); return; }
        if (busy || scrollForm is { IsDisposed: false }) return;
        PageTransition? transition = PreparePageChange(workspace, true);
        // Release our registrations while recording, so existing combinations can be reassigned.
        ReleaseCaptureHotkeys();
        SettingsPage page = new(preferences, scrollOptions, TrySaveSettings) { Dock = DockStyle.Fill };
        settingsPage = page;
        page.ReturnRequested += (_, _) => CloseSettings();
        page.AppearancePreview += mode => ApplyPalette(CapturePalette.For(mode));
        page.MotionPreview += enabled => UiMotion.UserEnabled = enabled;
        workspace.Visible = false;
        Controls.Add(page);
        if (transition == null) page.BringToFront();
        else Controls.SetChildIndex(page, 1);
        page.Select();
        AnimatePageChange(transition, page);
    }

    private void CloseSettings()
    {
        SettingsPage? page = settingsPage;
        if (page == null) return;
        PageTransition? transition = PreparePageChange(page, false);
        settingsPage = null;
        workspace.Visible = true;
        settingsButton.Focus();
        // Never unparent a visible page: Windows can briefly activate its detached HWND.
        page.Hide();
        page.Dispose();
        UiLanguage.Apply(preferences.Language);
        ApplyWorkspaceLanguage();
        ApplyAppearance();
        RegisterCaptureHotkeys();
        AnimatePageChange(transition, workspace);
    }

    private string? TrySavePreferences(ApplicationPreferences candidate) => TrySaveSettings(candidate, null);

    private string? TrySaveSettings(ApplicationPreferences candidate, ScrollingCaptureOptions? scrollingCandidate)
    {
        if (candidate.ValidateSaveFolder() is string folderError) return folderError;
        List<string> errors = new();
        foreach (CaptureShortcut action in ApplicationPreferences.Actions)
        {
            Keys keys = candidate.Shortcuts[action.Kind];
            if (!ApplicationPreferences.IsSupported(keys)) errors.Add(action.Title + L("InvalidShortcutError"));
        }
        foreach (var group in ApplicationPreferences.Actions.Where(a => candidate.Shortcuts[a.Kind] != Keys.None)
            .GroupBy(a => candidate.Shortcuts[a.Kind]).Where(group => group.Count() > 1))
            errors.Add(string.Join(L("ListSeparator"), group.Select(a => a.Title)) + L("DuplicateShortcutError"));
        if (errors.Count > 0) return string.Join(Environment.NewLine, errors);

        ReleaseCaptureHotkeys();
        string? temporaryOptions = null;
        try
        {
            foreach (CaptureShortcut action in ApplicationPreferences.Actions)
            {
                Keys keys = candidate.Shortcuts[action.Kind];
                if (keys == Keys.None) continue;
                HotkeyInfo hotkey = new(keys);
                RegisterHotkey(hotkey);
                hotkeys.Add(hotkey);
                if (hotkey.Status != HotkeyStatus.Registered)
                    errors.Add(LF("ShortcutTaken", action.Title, hotkey));
            }
            if (errors.Count > 0) return string.Join(Environment.NewLine, errors);
            byte[]? previousPreferences = null;
            if (scrollingCandidate != null)
            {
                // Prepare both files before committing; leave active capture options alone on errors.
                temporaryOptions = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporaryOptions, JsonConvert.SerializeObject(scrollingCandidate, Formatting.Indented));
                if (File.Exists(preferencesPath)) previousPreferences = File.ReadAllBytes(preferencesPath);
            }
            candidate.Save(preferencesPath);
            if (temporaryOptions != null)
            {
                try { File.Move(temporaryOptions, settingsPath, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (previousPreferences == null) File.Delete(preferencesPath);
                    else File.WriteAllBytes(preferencesPath, previousPreferences);
                    throw;
                }
                scrollOptions = scrollingCandidate!;
            }
            preferences = candidate.Clone();
            preferencesWarning = null;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return L("SavePreferencesFailed") + ex.Message;
        }
        finally
        {
            ReleaseCaptureHotkeys();
            if (temporaryOptions != null && File.Exists(temporaryOptions)) File.Delete(temporaryOptions);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Oemcomma)) { OpenSettings(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
