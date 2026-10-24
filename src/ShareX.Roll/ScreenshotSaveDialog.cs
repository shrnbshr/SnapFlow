// GPL-3.0-or-later. One save-window policy for captures and editor exports; encoding stays in ShareX.
using ShareX.HelpersLib;
using static ShareX.Roll.Localization.UiText;

namespace ShareX.Roll;

internal static class ScreenshotSaveDialog
{
    internal static SaveFileDialog Create(ApplicationPreferences preferences, string filePath)
    {
        string? folder = null;
        bool unavailable = preferences.UseDefaultSaveFolder && preferences.ValidateSaveFolder() != null;
        if (preferences.UseDefaultSaveFolder && !unavailable) folder = preferences.DefaultSaveFolder;
        else if (Directory.Exists(HelpersOptions.LastSaveDirectory)) folder = HelpersOptions.LastSaveDirectory;
        else if (Directory.Exists(Path.GetDirectoryName(filePath))) folder = Path.GetDirectoryName(filePath);

        int format = Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".jpe" or ".jfif" => 2,
            ".gif" => 3, ".bmp" => 4, ".tif" or ".tiff" => 5, _ => 1
        };
        return new SaveFileDialog
        {
            Title = L(unavailable ? "SaveFolderUnavailableTitle" : "SaveScreenshot"),
            Filter = "PNG (*.png)|*.png|JPEG (*.jpg, *.jpeg, *.jpe, *.jfif)|*.jpg;*.jpeg;*.jpe;*.jfif|GIF (*.gif)|*.gif|BMP (*.bmp)|*.bmp|TIFF (*.tif, *.tiff)|*.tif;*.tiff",
            DefaultExt = "png", FilterIndex = format,
            FileName = Path.GetFileName(filePath), InitialDirectory = folder ?? ""
        };
    }

    internal static string? Save(Image image, ApplicationPreferences preferences, string filePath, IWin32Window? owner = null)
    {
        using SaveFileDialog dialog = Create(preferences, filePath);
        if (dialog.ShowDialog(owner) != DialogResult.OK || string.IsNullOrEmpty(dialog.FileName)) return null;
        if (!ImageHelpers.SaveImage(image, dialog.FileName)) return null;
        HelpersOptions.LastSaveDirectory = Path.GetDirectoryName(dialog.FileName);
        return dialog.FileName;
    }
}
