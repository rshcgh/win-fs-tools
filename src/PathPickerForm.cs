namespace WinFsTools;

public sealed class PathPickerForm : Form
{
    private sealed record PathEntry(string DisplayName, string FullPath, bool IsDirectory);

    private readonly bool allowFiles;
    private readonly bool darkMode;
    private readonly TextBox pathBox = new();
    private readonly Label locationLabel = new();
    private readonly NoFocusListBox entries = new();
    private readonly NoFocusButton goButton = new();
    private readonly NoFocusButton upButton = new();
    private readonly NoFocusButton cancelButton = new();
    private readonly NoFocusButton chooseButton = new();
    private string currentDirectory;

    private readonly record struct Palette(Color Surface, Color Card, Color Input, Color Ink, Color Muted, Color Button, Color Accent);

    private static readonly Palette LightPalette = new(
        Color.FromArgb(248, 249, 251),
        Color.White,
        Color.White,
        Color.FromArgb(30, 34, 40),
        Color.FromArgb(96, 104, 114),
        Color.FromArgb(232, 235, 239),
        Color.FromArgb(35, 111, 210));

    private static readonly Palette DarkPalette = new(
        Color.FromArgb(24, 27, 32),
        Color.FromArgb(33, 38, 45),
        Color.FromArgb(43, 49, 58),
        Color.FromArgb(237, 242, 247),
        Color.FromArgb(165, 176, 188),
        Color.FromArgb(55, 62, 72),
        Color.FromArgb(64, 140, 233));

    public string SelectedPath { get; private set; } = string.Empty;

    public PathPickerForm(string initialPath, bool allowFiles, bool darkMode)
    {
        this.allowFiles = allowFiles;
        this.darkMode = darkMode;
        currentDirectory = GetInitialDirectory(initialPath);
        Text = allowFiles ? "choose input path" : "choose output folder";
        Icon = SystemIcons.Application;
        ShowIcon = true;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(760, 520);
        Font = new Font("Segoe UI", 10f);

        BuildLayout();
        WireEvents();
        ApplyTheme();
        RefreshEntries();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, Padding = new Padding(16), Margin = new Padding(0) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        Controls.Add(root);

        pathBox.Text = currentDirectory;
        pathBox.Dock = DockStyle.Fill;
        pathBox.Margin = new Padding(0, 3, 8, 3);
        pathBox.BorderStyle = BorderStyle.FixedSingle;
        root.Controls.Add(pathBox, 0, 0);
        ConfigureButton(goButton, "go");
        root.Controls.Add(goButton, 1, 0);
        ConfigureButton(upButton, "up");
        root.Controls.Add(upButton, 2, 0);

        locationLabel.AutoEllipsis = true;
        locationLabel.Dock = DockStyle.Fill;
        locationLabel.TextAlign = ContentAlignment.MiddleLeft;
        locationLabel.Tag = "muted";
        root.Controls.Add(locationLabel, 0, 1);
        root.SetColumnSpan(locationLabel, 3);

        entries.Dock = DockStyle.Fill;
        entries.BorderStyle = BorderStyle.FixedSingle;
        entries.IntegralHeight = false;
        entries.DisplayMember = nameof(PathEntry.DisplayName);
        entries.Margin = new Padding(0, 0, 0, 8);
        root.Controls.Add(entries, 0, 2);
        root.SetColumnSpan(entries, 3);

        ConfigureButton(cancelButton, "cancel");
        cancelButton.DialogResult = DialogResult.Cancel;
        root.Controls.Add(cancelButton, 1, 3);
        ConfigureButton(chooseButton, allowFiles ? "choose" : "choose folder");
        root.Controls.Add(chooseButton, 2, 3);
        AcceptButton = chooseButton;
        CancelButton = cancelButton;
    }

    private void WireEvents()
    {
        goButton.Click += (_, _) => NavigateTypedPath();
        upButton.Click += (_, _) => NavigateUp();
        chooseButton.Click += (_, _) => AcceptPath();
        entries.SelectedIndexChanged += (_, _) =>
        {
            if (entries.SelectedItem is PathEntry entry) pathBox.Text = entry.FullPath;
        };
        entries.DoubleClick += (_, _) =>
        {
            if (entries.SelectedItem is not PathEntry entry) return;
            if (entry.IsDirectory)
            {
                currentDirectory = entry.FullPath;
                RefreshEntries();
            }
            else if (allowFiles)
            {
                AcceptPath();
            }
        };
    }

    private void NavigateTypedPath()
    {
        var candidate = GetFullPathOrEmpty(pathBox.Text);
        if (Directory.Exists(candidate))
        {
            currentDirectory = candidate;
            RefreshEntries();
            return;
        }

        if (allowFiles && File.Exists(candidate))
        {
            AcceptPath();
            return;
        }

        ShowError("that path does not exist");
    }

    private void NavigateUp()
    {
        var parent = Directory.GetParent(currentDirectory);
        if (parent is not null)
        {
            currentDirectory = parent.FullName;
            RefreshEntries();
        }
    }

    private void RefreshEntries()
    {
        entries.BeginUpdate();
        try
        {
            entries.Items.Clear();
            locationLabel.Text = currentDirectory;
            var enumeration = new EnumerationOptions { IgnoreInaccessible = true, ReturnSpecialDirectories = false, RecurseSubdirectories = false };
            var directories = Directory.EnumerateDirectories(currentDirectory, "*", enumeration)
                .Select(path => new PathEntry($"[folder] {Path.GetFileName(path)}", path, true))
                .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase);
            foreach (var directory in directories) entries.Items.Add(directory);
            if (allowFiles)
            {
                var files = Directory.EnumerateFiles(currentDirectory, "*", enumeration)
                    .Select(path => new PathEntry(Path.GetFileName(path), path, false))
                    .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase);
                foreach (var file in files) entries.Items.Add(file);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowError($"could not read this folder: {exception.Message.ToLowerInvariant()}");
        }
        finally
        {
            entries.EndUpdate();
        }
    }

    private void AcceptPath()
    {
        var candidate = GetFullPathOrEmpty(pathBox.Text);
        if (candidate.Length == 0)
        {
            ShowError("enter a path");
            return;
        }

        if (Directory.Exists(candidate) || (allowFiles && File.Exists(candidate)))
        {
            SelectedPath = candidate;
            DialogResult = DialogResult.OK;
            return;
        }

        if (!allowFiles && !File.Exists(candidate))
        {
            var parent = Path.GetDirectoryName(candidate);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                SelectedPath = candidate;
                DialogResult = DialogResult.OK;
                return;
            }
        }

        ShowError("choose an existing path or a folder inside an existing folder");
    }

    private static string GetInitialDirectory(string initialPath)
    {
        var candidate = GetFullPathOrEmpty(initialPath);
        if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
        if (Directory.Exists(candidate)) return candidate;
        var parent = Path.GetDirectoryName(candidate);
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)) return parent;
        return Environment.CurrentDirectory;
    }

    private static string GetFullPathOrEmpty(string value)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : Path.GetFullPath(value.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    private void ApplyTheme()
    {
        var palette = darkMode ? DarkPalette : LightPalette;
        BackColor = palette.Surface;
        ForeColor = palette.Ink;
        pathBox.BackColor = palette.Input;
        pathBox.ForeColor = palette.Ink;
        entries.BackColor = palette.Input;
        entries.ForeColor = palette.Ink;
        locationLabel.ForeColor = palette.Muted;
        foreach (var button in new[] { goButton, upButton, cancelButton, chooseButton })
        {
            button.BackColor = button == chooseButton ? palette.Accent : palette.Button;
            button.ForeColor = button == chooseButton ? Color.White : palette.Ink;
        }
    }

    private static void ConfigureButton(Button button, string text)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 3, 0, 3);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Font = new Font("Segoe UI Semibold", 9.5f);
        button.Cursor = Cursors.Hand;
        button.TabStop = false;
    }

    private static void ShowError(string message) => MessageBox.Show(message, "win fs tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
