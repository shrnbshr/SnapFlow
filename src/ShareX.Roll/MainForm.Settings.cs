using static ShareX.Roll.Localization.UiText;
// GPL-3.0-or-later. Settings and shortcut routing only; capture implementations stay unchanged.
using Newtonsoft.Json;
using System.Diagnostics;
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
    private SettingsPage? cachedSettingsPage;
    private string? cachedSettingsCulture;
    private Bitmap? cachedSettingsSnapshot;
    private int cachedSettingsSnapshotSignature;
    private bool cachedSettingsSnapshotGlass;
    private string? preferencesWarning;
    internal (double Preparation, double Construction, double Attachment, double Snapshot) SettingsOpenTiming { get; private set; }

    private SettingsPage ReadySettingsPage()
    {
        if (cachedSettingsPage is { IsDisposed: false } && cachedSettingsCulture == UiLanguage.Culture.Name)
            return cachedSettingsPage;
        cachedSettingsPage?.Dispose();
        cachedSettingsSnapshot?.Dispose(); cachedSettingsSnapshot = null;
        SettingsPage page = new(preferences, scrollOptions, TrySaveSettings) { Dock = DockStyle.Fill };
        cachedSettingsPage = page;
        cachedSettingsCulture = UiLanguage.Culture.Name;
        page.ReturnRequested += (_, _) => CloseSettings();
        page.AppearancePreview += PreviewAppearance;
        page.MotionPreview += enabled => UiMotion.UserEnabled = enabled;
        page.TransparencyPreview += enabled =>
        {
            EndPageTransition();
            transparencyPreview = enabled;
            ApplyWindowBackdrop();
        };
        Controls.Add(page);
        Controls.SetChildIndex(page, Controls.Count - 1);
        return page;
    }

    private void InitializeSettingsPage()
    {
        // Prepare the native controls during window initialization, before accepting input.
        ReadySettingsPage();
        Shown += (_, _) =>
        {
            if (settingsPage != null || cachedSettingsPage == null) return;
            cachedSettingsPage.CreateControl();
            using (Bitmap? snapshot = SnapshotPage(cachedSettingsPage))
                if (snapshot != null) RememberSettingsSnapshot(cachedSettingsPage, snapshot);
            cachedSettingsPage.Hide();
        };
        Disposed += (_, _) => { cachedSettingsSnapshot?.Dispose(); cachedSettingsSnapshot = null; };
    }

    private int SettingsSnapshotSignature(Control page)
    {
        // Reuse only a restored page with the same content, geometry and material.
        // Hover/focus feedback remains live after the transition has completed.
        HashCode hash = new();
        hash.Add(appearancePalette); hash.Add(page.DeviceDpi); hash.Add(UiLanguage.Culture.Name);
        hash.Add(WindowBackdrop.IsSupported()); hash.Add(SystemInformation.HighContrast);
        void Include(Control control)
        {
            if (!control.Visible) return;
            hash.Add(control.GetType()); hash.Add(control.Bounds); hash.Add(control.Padding); hash.Add(control.Margin);
            hash.Add(control.Text); hash.Add(control.Font.Name); hash.Add(control.Font.Size); hash.Add(control.Font.Style);
            hash.Add(control.BackColor); hash.Add(control.ForeColor); hash.Add(control.Enabled); hash.Add(control.RightToLeft);
            if (control is CheckBox check) hash.Add(check.Checked);
            if (control is RadioButton radio) hash.Add(radio.Checked);
            if (control is ComboBox choice) hash.Add(choice.SelectedIndex);
            if (control is NumericUpDown number) hash.Add(number.Value);
            if (control is ScrollableControl scroll) hash.Add(scroll.AutoScrollPosition);
            foreach (Control child in control.Controls) Include(child);
        }
        Include(page);
        return hash.ToHashCode();
    }

    private Bitmap? SettingsSnapshot(Control page)
    {
        if (cachedSettingsSnapshot == null || cachedSettingsSnapshot.Size != page.Size ||
            cachedSettingsSnapshotGlass != (windowBackdrop?.IsActive == true) ||
            cachedSettingsSnapshotSignature != SettingsSnapshotSignature(page)) return null;
        Bitmap copy = (Bitmap)cachedSettingsSnapshot.Clone();
        windowBackdrop?.CopySnapshot(cachedSettingsSnapshot, copy);
        return copy;
    }

    private void RememberSettingsSnapshot(Control page, Bitmap snapshot)
    {
        Bitmap copy = (Bitmap)snapshot.Clone();
        windowBackdrop?.CopySnapshot(snapshot, copy);
        cachedSettingsSnapshot?.Dispose();
        cachedSettingsSnapshot = copy;
        cachedSettingsSnapshotSignature = SettingsSnapshotSignature(page);
        cachedSettingsSnapshotGlass = windowBackdrop?.IsActive == true;
    }

    private void PreviewAppearance(AppearanceMode mode)
    {
        CapturePalette palette = CapturePalette.For(mode);
        if (palette == appearancePalette) { ApplyPalette(palette); return; }
        Control page = settingsPage ?? (Control)workspace;
        PageTransition? transition = PreparePageChange(page, true, crossFade: true);
        ApplyPalette(palette);
        settingsPage?.SettleThemeSelection();
        AnimatePageChange(transition, page);
    }

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
        long started = Stopwatch.GetTimestamp();
        PageTransition? transition = PreparePageChange(workspace, true);
        // Release our registrations while recording, so existing combinations can be reassigned.
        ReleaseCaptureHotkeys();
        long prepared = Stopwatch.GetTimestamp();
        SettingsPage page = ReadySettingsPage();
        page.RestorePreferences(preferences, scrollOptions);
        long constructed = Stopwatch.GetTimestamp();
        settingsPage = page;
        workspace.Visible = false;
        page.Show();
        if (transition == null) page.BringToFront();
        else Controls.SetChildIndex(page, 1);
        page.Select();
        long attached = Stopwatch.GetTimestamp();
        AnimatePageChange(transition, page, cacheSettings: true);
        long finished = Stopwatch.GetTimestamp();
        double milliseconds = 1000d / Stopwatch.Frequency;
        SettingsOpenTiming = ((prepared - started) * milliseconds, (constructed - prepared) * milliseconds,
            (attached - constructed) * milliseconds, (finished - attached) * milliseconds);
    }

    private void CloseSettings()
    {
        SettingsPage? page = settingsPage;
        if (page == null) return;
        // Restore the saved material before taking the return snapshot, so its alpha mask matches the live page.
        transparencyPreview = null;
        ApplyWindowBackdrop();
        PageTransition? transition = PreparePageChange(page, false);
        settingsPage = null;
        workspace.Visible = true;
        settingsButton.Focus();
        // Never unparent a visible page: Windows can briefly activate its detached HWND.
        page.Hide();
        UiLanguage.Apply(preferences.Language);
        SettingsPage ready = ReadySettingsPage();
        ready.Hide();
        ready.RestorePreferences(preferences, scrollOptions, refreshAppearance: false);
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
