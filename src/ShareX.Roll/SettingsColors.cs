// GPL-3.0-or-later. One appearance pass for embedded settings and the scrolling options window.
namespace ShareX.Roll;

internal static class SettingsColors
{
    internal static void Apply(Control root, CapturePalette palette, Button primary, Func<bool>? menuTransparencyEnabled = null)
    {
        void Paint(Control control, Color background)
        {
            if (control is SettingsGroup group) { background = palette.Surface; group.Palette = palette; group.Invalidate(); }
            control.BackColor = background;
            control.ForeColor = Equals(control.Tag, "secondary") ? palette.Secondary : palette.Text;
            if (control.Name == "aboutDivider") control.BackColor = palette.Border;
            if (control is SettingsFolderField folderField) { folderField.ApplyPalette(palette); return; }
            if (control is SettingsNumberField number) { number.ApplyPalette(palette); return; }
            if (control is SettingsChoice choice) { choice.ApplyPalette(palette, menuTransparencyEnabled); return; }
            if (control is AboutLinkButton aboutLink) { aboutLink.ApplyPalette(palette); return; }
            if (control is AppMark mark) { mark.Palette = palette; mark.Invalidate(); }
            if (control is LinkLabel link)
            {
                link.LinkColor = link.VisitedLinkColor = palette.Accent;
                link.ActiveLinkColor = palette.Text;
            }
            if (control is ShortcutRecorder recorder) { recorder.Palette = palette; recorder.Invalidate(); }
            else if (control is ShortcutClearButton clear) { clear.Palette = palette; clear.Invalidate(); }
            else if (control is SettingsDisclosure disclosure) { disclosure.Palette = palette; disclosure.Invalidate(); }
            else if (control is Button button)
            {
                string? accessibleName = button.AccessibleName;
                palette.StyleButton(button, ReferenceEquals(button, primary));
                if (button.Text == "×") button.AccessibleName = accessibleName;
            }
            if (control is ThemeOption theme) { theme.Palette = palette; theme.Invalidate(); }
            if (control is MotionToggle toggle) { toggle.Palette = palette; toggle.Invalidate(); }
            foreach (Control child in control.Controls) Paint(child, background);
        }
        Paint(root, palette.Background);
    }
}
