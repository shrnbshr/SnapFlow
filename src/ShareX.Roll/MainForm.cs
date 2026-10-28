using static ShareX.Roll.Localization.UiText;
// GPL-3.0-or-later. ShareX Team's original capture implementations are in the sibling projects.
using Newtonsoft.Json;
using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;

namespace ShareX.Roll;

public sealed partial class MainForm : HotkeyForm
{
    private readonly PreviewCanvas preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
    private readonly Button copy = new() { Name = "copyButton", Text = L("CopyImage"), AutoSize = true, Enabled = false };
    private readonly Button save = new() { Name = "saveButton", Text = L("SaveAs"), AutoSize = true, Enabled = false };
    private readonly Button edit = new() { Name = "editButton", Text = L("Edit"), AutoSize = true, Enabled = false };
    private readonly List<HotkeyInfo> hotkeys = new();
    private readonly string settingsPath = Path.Combine(AppContext.BaseDirectory, "SnapFlow.settings.json");
    private ScrollingCaptureOptions scrollOptions;
    private ScrollingCaptureForm? scrollForm;
    private RegionCaptureForm? editorForm;
    private Bitmap? result;
    private bool busy;

    public MainForm()
    {
        SuspendLayout();
        Text = "SnapFlow";
        Name = "MainForm";
        Icon = ShareXResources.Icon;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(960, 720);
        MinimumSize = new Size(700, 580);
        StartPosition = FormStartPosition.CenterScreen;
        scrollOptions = LoadOptions(); // No overrides: use the released version's defaults.
        LoadPreferences();
        Load += (_, _) => RegisterCaptureHotkeys();

        InitializeAppearance();
        InitializeSettingsPage();
        copy.Click += (_, _) => { if (result != null) ClipboardHelpers.CopyImage(result); };
        save.Click += (_, _) => { if (result != null) SaveImage(result); };
        edit.Click += (_, _) => EditResult();
        HotkeyPress += async (id, _, _) => await DispatchCaptureHotkeyAsync(id);
        Shown += (_, _) =>
        {
            ActiveControl = null;
        };
        FormClosing += (_, e) =>
        {
            if (scrollForm is { IsDisposed: false })
            {
                e.Cancel = true;
                scrollForm.ForceActivate(); // Original window activation stops the capture.
            }
        };
        FormClosed += (_, _) =>
        {
            foreach (HotkeyInfo key in hotkeys) UnregisterHotkey(key);
            PersistOptions();
            preview.Image = null;
            result?.Dispose();
        };
        ResumeLayout(true);
    }

    public async Task CaptureAsync(string kind)
    {
        if (busy || editorForm is { IsDisposed: false }) return;
        if (scrollForm is { IsDisposed: false })
        {
            if (kind == "scroll") await scrollForm.StartStopScrollingCapture();
            return;
        }
        busy = true;
        Hide();
        try
        {
            await Task.Delay(250);
            if (kind == "scroll")
            {
                await ScrollingCaptureForm.StartStopScrollingCapture(scrollOptions, image =>
                {
                    using (image) SaveImage(image);
                }, windowCreated: AttachScrollingForm);
                return;
            }

            Bitmap? bitmap = null;
            if (kind is "region" or "window") bitmap = RegionCaptureTasks.GetRegionImage();
            else bitmap = new Screenshot().CaptureFullscreen();
            if (bitmap != null) SetPreview(bitmap);
        }
        catch (Exception ex)
        {
            status.Text = L("CaptureFailed") + ex.Message;
        }
        finally
        {
            busy = false;
            if (scrollForm == null && !IsDisposed) { Show(); this.ForceActivate(); }
        }
    }

    private void AttachScrollingForm(ScrollingCaptureForm form)
    {
        // Subscribe before Show/Load opens the modal selector, so a fast cancel/close cannot be missed.
        scrollForm = form;
        string originalTitle = L("ScrollTitle");
        form.Text = originalTitle;
        form.Activated += (_, _) =>
        {
            if (!form.IsDisposed) form.Text = form.NoScrollDetected
                ? L("SingleScreenTitle") : originalTitle;
        };
        Control? saveButton = form.Controls.Find("btnUpload", true).FirstOrDefault();
        if (saveButton != null) saveButton.Text = L("SaveImage");
        ApplyScrollingAppearance(form);
        form.CaptureCompleted += image =>
        {
            if (image != null) SetPreview((Bitmap)image.Clone());
            else status.Text = L("CaptureFailed");
            if (form.NoScrollDetected) status.Text = L("SingleScreenTitle");
            // Let StartCapture/SelectWindow unwind before disposing their controls and manager.
            form.BeginInvoke((Action)form.Close);
        };
        form.SelectionCancelled += () => form.BeginInvoke((Action)form.Close);
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(scrollForm, form)) scrollForm = null;
            PersistOptions();
            if (!IsDisposed) { Show(); this.ForceActivate(); }
        };
    }

    private void SetPreview(Bitmap bitmap)
    {
        Bitmap? previous = result;
        preview.Image = bitmap;
        preview.SynchronizeImage();
        result = bitmap;
        previous?.Dispose();
        copy.Enabled = save.Enabled = edit.Enabled = true;
        RefreshPreviewToolbar();
        status.Text = string.Empty;
    }

    private void EditResult()
    {
        if (result == null || busy || editorForm is { IsDisposed: false }) return;
        ReleaseCaptureHotkeys();
        try
        {
            using RegionCaptureForm dialog = ScreenshotEditor.Create(result, appearancePalette, preferences);
            editorForm = dialog;
            dialog.ShowDialog(this);
            // Enter is the upstream editor's keyboard equivalent of the apply button.
            if (dialog.Result is RegionResult.AnnotateRunAfterCaptureTasks or RegionResult.Region)
                SetPreview(dialog.GetResultImage());
        }
        catch (Exception ex)
        {
            status.Text = L("EditorFailed") + ex.Message;
        }
        finally
        {
            editorForm = null;
            if (!IsDisposed && !Disposing)
            {
                string message = status.Text;
                RegisterCaptureHotkeys();
                status.Text = message;
            }
        }
    }

    private void SaveImage(Bitmap bitmap) => ScreenshotSaveDialog.Save(bitmap, preferences,
        LF("ScreenshotFile", DateTime.Now.ToString("yyyyMMdd_HHmmss")), Visible ? this : scrollForm);

    private ScrollingCaptureOptions LoadOptions()
    {
        try
        {
            string path = File.Exists(settingsPath) ? settingsPath : Path.Combine(AppContext.BaseDirectory, "ShareX - roll.settings.json");
            if (File.Exists(path)) return JsonConvert.DeserializeObject<ScrollingCaptureOptions>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            status.Text = L("OptionsReadFailed") + ex.Message;
        }
        return new ScrollingCaptureOptions();
    }

    private void PersistOptions()
    {
        try { File.WriteAllText(settingsPath, JsonConvert.SerializeObject(scrollOptions, Formatting.Indented)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.Text = L("OptionsSaveFailed") + ex.Message;
        }
    }
}
