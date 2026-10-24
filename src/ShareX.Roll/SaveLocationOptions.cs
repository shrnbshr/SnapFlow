// GPL-3.0-or-later. A default folder changes where the save window opens, never when an image is saved.
using static ShareX.Roll.Localization.UiText;

namespace ShareX.Roll;

internal sealed class SaveLocationOptions : UserControl
{
    private readonly ApplicationPreferences draft;
    private readonly MotionToggle useFolder = new() { Name = "defaultSaveFolderToggle", Size = new Size(46, 28), Anchor = AnchorStyles.Right, Margin = Padding.Empty };
    private readonly TextBox folder = new() { Name = "defaultSaveFolderPath", ReadOnly = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
    private readonly Button browse = new() { Name = "browseSaveFolderButton", AutoSize = true, MinimumSize = new Size(100, 38), Padding = new Padding(8, 0, 8, 0), Margin = Padding.Empty, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
    private readonly TableLayoutPanel pathRow = new() { Name = "saveFolderPathRow", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = 38, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 12, 0, 0) };
    private readonly Label validation = new() { Name = "saveFolderValidation", AutoSize = true, Dock = DockStyle.Top, Tag = "secondary", Margin = new Padding(0, 8, 0, 0) };
    internal event EventHandler? Changed;

    internal SaveLocationOptions(ApplicationPreferences draft)
    {
        SuspendLayout();
        this.draft = draft;
        Name = "saveLocationOptions";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        SettingsGroup group = new() { Name = "saveLocationGroup", Dock = DockStyle.Top, AutoSize = true, DrawDividers = false, ColumnCount = 1, RowCount = 3, Padding = new Padding(16, 14, 16, 14), Margin = Padding.Empty };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        group.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        group.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        group.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        TableLayoutPanel header = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        FlowLayoutPanel caption = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 14, 0) };
        Label title = new() { Text = L("DefaultSaveFolder"), AutoSize = true, Margin = Padding.Empty };
        Label hint = new() { Text = L("DefaultSaveFolderHint"), AutoSize = true, Tag = "secondary", Font = new Font("Microsoft YaHei UI", 10), Margin = new Padding(0, 3, 0, 0) };
        caption.Controls.AddRange([title, hint]);
        caption.SizeChanged += (_, _) =>
        {
            foreach (Label label in caption.Controls) label.MaximumSize = new Size(Math.Max(1, caption.ClientSize.Width), 0);
        };
        useFolder.AccessibleName = L("DefaultSaveFolder");
        useFolder.AccessibleDescription = L("DefaultSaveFolderHint");
        useFolder.Checked = draft.UseDefaultSaveFolder;
        useFolder.CheckedChanged += (_, _) =>
        {
            draft.UseDefaultSaveFolder = useFolder.Checked;
            RefreshState();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        header.Controls.Add(caption, 0, 0);
        header.Controls.Add(useFolder, 1, 0);
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folder.AccessibleName = L("SaveFolderPath");
        folder.PlaceholderText = L("ChooseSaveFolder");
        folder.AccessibleDescription = L("DefaultSaveFolderHint");
        folder.Font = new Font("Microsoft YaHei UI", 10);
        folder.Text = draft.DefaultSaveFolder;
        folder.TextChanged += (_, _) => RefreshState();
        browse.Text = L("BrowseSaveFolder");
        browse.AccessibleName = L("BrowseSaveFolder");
        browse.Click += (_, _) => Browse();
        SettingsFolderField field = new(folder) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
        pathRow.Controls.Add(field, 0, 0);
        pathRow.Controls.Add(browse, 1, 0);
        pathRow.Visible = useFolder.Checked;
        group.Controls.Add(header, 0, 0);
        group.Controls.Add(pathRow, 0, 1);
        validation.Font = new Font("Microsoft YaHei UI", 10);
        group.Controls.Add(validation, 0, 2);
        Controls.Add(group);
        group.SizeChanged += (_, _) => validation.MaximumSize = new Size(Math.Max(1, group.ClientSize.Width - group.Padding.Horizontal), 0);
        RefreshState();
        ResumeLayout(true);
    }

    private void Browse()
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = L("ChooseSaveFolder"), UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(draft.DefaultSaveFolder) ? draft.DefaultSaveFolder : ""
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        folder.Text = draft.DefaultSaveFolder = dialog.SelectedPath;
        RefreshState();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void LoadPreferences(ApplicationPreferences preferences)
    {
        folder.Text = draft.DefaultSaveFolder = preferences.DefaultSaveFolder;
        useFolder.Checked = draft.UseDefaultSaveFolder = preferences.UseDefaultSaveFolder;
        RefreshState();
    }

    internal void FocusFolder() { pathRow.Visible = true; browse.Focus(); }

    private void RefreshState()
    {
        // Reveal the dependency once per toggle, without a per-frame layout of the scrolling page.
        SuspendLayout();
        pathRow.Visible = useFolder.Checked;
        validation.Text = string.IsNullOrEmpty(folder.Text) ? L("ChooseSaveFolder") : L("SaveFolderInvalid");
        validation.Visible = useFolder.Checked && !string.IsNullOrEmpty(folder.Text) && draft.ValidateSaveFolder() != null;
        ResumeLayout(true);
    }
}
