namespace WinFsTools;

public sealed class MainForm : Form
{
    private sealed record ThemePalette(Color Surface, Color Card, Color Input, Color Log, Color Ink, Color Muted, Color Button, Color Accent);

    private readonly FileOperationEngine engine = new();
    private readonly TextBox inputPath = new();
    private readonly TextBox outputPath = new();
    private readonly NoFocusCheckBox recursive = new();
    private readonly NoFocusCheckBox deleteParentIfIdentical = new();
    private readonly ChoiceButton operation = new();
    private readonly TextBox extensions = new();
    private readonly NoFocusCheckBox backupBeforeDelete = new();
    private readonly ChoiceButton sortMode = new();
    private readonly NoFocusCheckBox moveInsteadOfCopy = new();
    private readonly NoFocusCheckBox themeToggle = new();
    private readonly ProgressBar progressBar = new();
    private readonly Label progressLabel = new();
    private readonly Label statusLabel = new();
    private readonly TextBox logBox = new();
    private readonly NoFocusButton runButton = new();
    private readonly NoFocusButton cancelButton = new();
    private readonly List<Panel> cards = [];
    private CancellationTokenSource? cancellation;
    private bool darkMode = true;
    private string? lastLoggedStatus;
    private int lastLoggedProgress = -10;

    private static readonly ThemePalette LightTheme = new(
        Color.FromArgb(248, 249, 251),
        Color.White,
        Color.White,
        Color.FromArgb(252, 252, 253),
        Color.FromArgb(30, 34, 40),
        Color.FromArgb(96, 104, 114),
        Color.FromArgb(232, 235, 239),
        Color.FromArgb(35, 111, 210));

    private static readonly ThemePalette DarkTheme = new(
        Color.FromArgb(24, 27, 32),
        Color.FromArgb(33, 38, 45),
        Color.FromArgb(43, 49, 58),
        Color.FromArgb(25, 29, 35),
        Color.FromArgb(237, 242, 247),
        Color.FromArgb(165, 176, 188),
        Color.FromArgb(55, 62, 72),
        Color.FromArgb(64, 140, 233));

    public MainForm()
    {
        Text = "win fs tools";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        ShowIcon = true;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 680);
        Size = new Size(1180, 780);
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildLayout();
        WireEvents();
        UpdateOperationFields();
        ApplyTheme();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(24),
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 166));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        var titlePanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        titlePanel.Controls.Add(new Label { Text = "win fs tools", AutoSize = true, Font = new Font("Segoe UI Semibold", 24f), Location = new Point(0, 0) });
        titlePanel.Controls.Add(new Label { Text = "small, fast posix file operations for windows; now with fish and awful ui!", AutoSize = true, ForeColor = Color.Empty, Tag = "muted", Location = new Point(3, 40) });
        heading.Controls.Add(titlePanel, 0, 0);
        themeToggle.Text = "light mode";
        themeToggle.AutoSize = true;
        themeToggle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        themeToggle.Margin = new Padding(0, 12, 0, 0);
        heading.Controls.Add(themeToggle, 1, 0);
        root.Controls.Add(heading, 0, 0);

        root.Controls.Add(BuildPathCard(), 0, 1);
        root.Controls.Add(BuildOperationCard(), 0, 2);
        root.Controls.Add(BuildProgressCard(), 0, 3);
        root.Controls.Add(BuildLogCard(), 0, 4);
    }

    private Control BuildPathCard()
    {
        var card = MakeCard();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Padding = new Padding(18, 12, 18, 10), Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        inputPath.PlaceholderText = "folder or file";
        outputPath.PlaceholderText = "folder, or zip/checksum file";
        ConfigureTextBox(inputPath);
        ConfigureTextBox(outputPath);
        layout.Controls.Add(MakeLabel("input path"), 0, 0);
        layout.Controls.Add(inputPath, 1, 0);
        layout.Controls.Add(MakeBrowseButton("browse", inputPath, false), 2, 0);
        layout.Controls.Add(MakeLabel("output path"), 0, 1);
        layout.Controls.Add(outputPath, 1, 1);
        layout.Controls.Add(MakeBrowseButton("browse", outputPath, true), 2, 1);
        recursive.Text = "include subfolders";
        recursive.AutoSize = true;
        recursive.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        recursive.Checked = true;
        recursive.Margin = new Padding(0, 3, 0, 0);
        deleteParentIfIdentical.Text = "delete parent if identical";
        deleteParentIfIdentical.AutoSize = true;
        deleteParentIfIdentical.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        deleteParentIfIdentical.Checked = true;
        deleteParentIfIdentical.Margin = new Padding(0, 3, 0, 0);
        var pathOptions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        pathOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        pathOptions.Controls.Add(recursive, 0, 0);
        pathOptions.Controls.Add(deleteParentIfIdentical, 1, 0);
        layout.Controls.Add(pathOptions, 1, 2);
        layout.SetColumnSpan(pathOptions, 2);
        return card;
    }

    private Control BuildOperationCard()
    {
        var card = MakeCard();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Padding = new Padding(18, 12, 18, 10), Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        operation.SetChoices("delete duplicates", "copy by extension", "move by extension", "delete by extension", "bulk compress", "bulk un-compress", "sort files", "create checksums");
        operation.SelectedIndex = 0;
        ConfigureChoiceButton(operation);
        layout.Controls.Add(MakeLabel("operation"), 0, 0);
        layout.Controls.Add(operation, 1, 0);
        layout.SetColumnSpan(operation, 2);

        extensions.PlaceholderText = "for example: .jpg, png, pdf";
        ConfigureTextBox(extensions);
        layout.Controls.Add(MakeLabel("extensions"), 0, 1);
        layout.Controls.Add(extensions, 1, 1);

        var sortPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        sortPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        sortPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sortPanel.Controls.Add(MakeLabel("sort by"), 0, 0);
        sortMode.SetChoices("extension", "modified month", "size band");
        sortMode.SelectedIndex = 0;
        ConfigureChoiceButton(sortMode);
        sortPanel.Controls.Add(sortMode, 1, 0);
        layout.Controls.Add(sortPanel, 2, 1);

        backupBeforeDelete.Text = "backup duplicates before deleting";
        backupBeforeDelete.AutoSize = true;
        backupBeforeDelete.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        backupBeforeDelete.Margin = new Padding(0, 3, 0, 0);
        moveInsteadOfCopy.Text = "move instead of copy";
        moveInsteadOfCopy.AutoSize = true;
        moveInsteadOfCopy.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        moveInsteadOfCopy.Margin = new Padding(0, 3, 0, 0);
        layout.Controls.Add(backupBeforeDelete, 1, 2);
        layout.Controls.Add(moveInsteadOfCopy, 2, 2);
        return card;
    }

    private Control BuildProgressCard()
    {
        var card = MakeCard();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(18, 12, 18, 10), Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        card.Controls.Add(layout);

        statusLabel.Text = "ready";
        statusLabel.Tag = "muted";
        statusLabel.AutoEllipsis = true;
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(statusLabel, 0, 0);
        progressLabel.Text = "0%";
        progressLabel.Dock = DockStyle.Fill;
        progressLabel.TextAlign = ContentAlignment.MiddleRight;
        layout.Controls.Add(progressLabel, 1, 0);
        progressBar.Dock = DockStyle.Fill;
        progressBar.Minimum = 0;
        progressBar.Maximum = 100;
        progressBar.Margin = new Padding(0, 5, 10, 5);
        layout.Controls.Add(progressBar, 0, 1);
        layout.SetColumnSpan(progressBar, 2);
        ConfigureButton(runButton, "run operation");
        layout.Controls.Add(runButton, 2, 0);
        ConfigureButton(cancelButton, "cancel");
        cancelButton.Enabled = false;
        layout.Controls.Add(cancelButton, 2, 1);
        return card;
    }

    private Control BuildLogCard()
    {
        var card = MakeCard();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(18, 12, 18, 14), Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        card.Controls.Add(layout);
        logBox.Multiline = true;
        logBox.ReadOnly = true;
        logBox.ScrollBars = ScrollBars.Vertical;
        logBox.BorderStyle = BorderStyle.FixedSingle;
        logBox.Dock = DockStyle.Fill;
        logBox.Font = new Font("Cascadia Mono", 9.5f);
        layout.Controls.Add(logBox, 0, 0);
        var clearButton = new NoFocusButton { Height = 34, Margin = new Padding(10, 0, 0, 0) };
        ConfigureButton(clearButton, "clear log");
        clearButton.Dock = DockStyle.Top;
        clearButton.Click += (_, _) => logBox.Clear();
        layout.Controls.Add(clearButton, 1, 0);
        return card;
    }

    private void WireEvents()
    {
        operation.SelectedIndexChanged += (_, _) => UpdateOperationFields();
        backupBeforeDelete.CheckedChanged += (_, _) => UpdateOperationFields();
        themeToggle.CheckedChanged += (_, _) =>
        {
            darkMode = !themeToggle.Checked;
            ApplyTheme();
        };
        runButton.Click += async (_, _) => await RunOperationAsync();
        cancelButton.Click += (_, _) => cancellation?.Cancel();
        FormClosing += OnFormClosing;
    }

    private void UpdateOperationFields()
    {
        var kind = GetSelectedKind();
        extensions.Enabled = kind is OperationKind.CopyByExtension or OperationKind.MoveByExtension or OperationKind.DeleteByExtension;
        sortMode.Enabled = kind == OperationKind.SortFiles;
        moveInsteadOfCopy.Enabled = true;
        backupBeforeDelete.Enabled = true;
        deleteParentIfIdentical.Enabled = true;
        outputPath.Enabled = kind is not OperationKind.DeleteByExtension && (kind != OperationKind.DeleteDuplicates || backupBeforeDelete.Checked);
    }

    private async Task RunOperationAsync()
    {
        if (!TryBuildOptions(out var options, out var error))
        {
            ShowError(error);
            return;
        }

        SetRunning(true);
        cancellation = new CancellationTokenSource();
        progressBar.Value = 0;
        progressLabel.Text = "0%";
        statusLabel.Text = "starting";
        lastLoggedStatus = null;
        lastLoggedProgress = -10;
        AppendLog($"starting {GetOperationName(options.Kind)}");
        AppendLog($"input: {options.InputPath}");
        AppendLog($"scope: {(options.Recursive ? "including subfolders" : "top folder only")}");
        if (options.Extensions.Count > 0) AppendLog($"extensions: {string.Join(", ", options.Extensions.Order(StringComparer.OrdinalIgnoreCase))}");
        if (!string.IsNullOrWhiteSpace(options.OutputPath)) AppendLog($"output: {options.OutputPath}");
        if (options.BackupBeforeDelete) AppendLog("duplicate backup: enabled");
        if (options.DeleteParentIfIdentical) AppendLog("duplicate-only parent folders: enabled");
        if (options.MoveInsteadOfCopy) AppendLog("sort mode: move files");

        try
        {
            var progress = new Progress<OperationProgress>(UpdateProgress);
            var result = await engine.RunAsync(options, progress, cancellation.Token);
            statusLabel.Text = "complete";
            progressBar.Value = 100;
            progressLabel.Text = "100%";
            AppendLog($"finished {GetOperationName(options.Kind)}: {result.Processed} processed, {result.Skipped} skipped, {result.Warnings.Count} warnings");
            foreach (var warning in result.Warnings.Take(30)) AppendLog($"warning: {warning}");
            if (result.Warnings.Count > 30) AppendLog($"warning: {result.Warnings.Count - 30} more warnings");
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "cancelled";
            AppendLog($"cancelled during {lastLoggedStatus ?? "startup"}; partial changes may have been made");
        }
        catch (Exception exception)
        {
            statusLabel.Text = "failed";
            AppendLog($"{GetOperationName(options.Kind)} failed during {lastLoggedStatus ?? "startup"}: {exception.Message.ToLowerInvariant()}");
            ShowError(exception.Message.ToLowerInvariant());
        }
        finally
        {
            cancellation.Dispose();
            cancellation = null;
            SetRunning(false);
        }
    }

    private void UpdateProgress(OperationProgress value)
    {
        progressBar.Value = value.Percent;
        progressLabel.Text = value.Total > 0 ? $"{value.Percent}% ({value.Current}/{value.Total})" : "working";
        statusLabel.Text = value.Total > 0 ? $"{value.Status} ({value.Current}/{value.Total})" : value.Status;
        var milestone = value.Total > 0 && (value.Percent >= lastLoggedProgress + 10 || value.Current >= value.Total);
        var phaseChanged = !string.Equals(lastLoggedStatus, value.Status, StringComparison.Ordinal);
        if (phaseChanged || milestone)
        {
            var progress = value.Total > 0 ? $" ({value.Current}/{value.Total})" : string.Empty;
            var path = string.IsNullOrWhiteSpace(value.CurrentPath) ? string.Empty : $" - {Path.GetFileName(value.CurrentPath)}";
            AppendLog($"{value.Status}{progress}{path}");
            lastLoggedStatus = value.Status;
            lastLoggedProgress = value.Percent;
        }
    }

    private bool TryBuildOptions(out OperationOptions options, out string error)
    {
        options = null!;
        error = string.Empty;
        var input = inputPath.Text.Trim();
        if (!File.Exists(input) && !Directory.Exists(input))
        {
            error = "choose an existing input path";
            return false;
        }

        var kind = GetSelectedKind();
        var parsedExtensions = ParseExtensions(extensions.Text);
        var extensionOperation = kind is OperationKind.CopyByExtension or OperationKind.MoveByExtension or OperationKind.DeleteByExtension;
        if (extensionOperation && parsedExtensions.Count == 0)
        {
            error = "enter at least one extension";
            return false;
        }

        var outputRequired = kind is OperationKind.CopyByExtension or OperationKind.MoveByExtension or OperationKind.BulkCompress or OperationKind.BulkUncompress or OperationKind.SortFiles or OperationKind.CreateChecksums || (kind == OperationKind.DeleteDuplicates && backupBeforeDelete.Checked);
        if (outputRequired && string.IsNullOrWhiteSpace(outputPath.Text))
        {
            error = "choose an output path for this operation";
            return false;
        }

        options = new OperationOptions
        {
            Kind = kind,
            InputPath = input,
            OutputPath = outputPath.Text.Trim(),
            Recursive = recursive.Checked,
            BackupBeforeDelete = backupBeforeDelete.Checked,
            DeleteParentIfIdentical = deleteParentIfIdentical.Checked,
            MoveInsteadOfCopy = moveInsteadOfCopy.Checked,
            SortMode = (SortMode)sortMode.SelectedIndex,
            Extensions = parsedExtensions
        };
        return true;
    }

    private static HashSet<string> ParseExtensions(string value)
    {
        return value.Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim().TrimStart('.').ToLowerInvariant())
            .Where(item => item.Length > 0)
            .Select(item => $".{item}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private OperationKind GetSelectedKind() => operation.SelectedIndex switch
    {
        1 => OperationKind.CopyByExtension,
        2 => OperationKind.MoveByExtension,
        3 => OperationKind.DeleteByExtension,
        4 => OperationKind.BulkCompress,
        5 => OperationKind.BulkUncompress,
        6 => OperationKind.SortFiles,
        7 => OperationKind.CreateChecksums,
        _ => OperationKind.DeleteDuplicates
    };

    private void SetRunning(bool running)
    {
        runButton.Enabled = !running;
        cancelButton.Enabled = running;
        inputPath.Enabled = !running;
        outputPath.Enabled = !running && (GetSelectedKind() != OperationKind.DeleteByExtension && (GetSelectedKind() != OperationKind.DeleteDuplicates || backupBeforeDelete.Checked));
        recursive.Enabled = !running;
        deleteParentIfIdentical.Enabled = !running;
        operation.Enabled = !running;
        themeToggle.Enabled = true;
        extensions.Enabled = !running && (GetSelectedKind() is OperationKind.CopyByExtension or OperationKind.MoveByExtension or OperationKind.DeleteByExtension);
        sortMode.Enabled = !running && GetSelectedKind() == OperationKind.SortFiles;
        moveInsteadOfCopy.Enabled = !running;
        backupBeforeDelete.Enabled = !running;
    }

    private void ApplyTheme()
    {
        var palette = darkMode ? DarkTheme : LightTheme;
        BackColor = palette.Surface;
        ForeColor = palette.Ink;
        themeToggle.Text = darkMode ? "light mode" : "dark mode";
        foreach (Control control in Controls) ApplyThemeTo(control, false, palette);
    }

    private void ApplyThemeTo(Control control, bool insideCard, ThemePalette palette)
    {
        var isCard = string.Equals(control.Tag as string, "card", StringComparison.Ordinal);
        insideCard |= isCard;
        if (control is Panel or TableLayoutPanel) control.BackColor = insideCard ? palette.Card : palette.Surface;
        if (control is Label label) label.ForeColor = string.Equals(label.Tag as string, "muted", StringComparison.Ordinal) ? palette.Muted : palette.Ink;
        if (control is TextBox textBox)
        {
            textBox.BackColor = textBox == logBox ? palette.Log : palette.Input;
            textBox.ForeColor = palette.Ink;
        }
        if (control is ChoiceButton choiceButton) choiceButton.ApplyTheme(palette.Input, palette.Ink, palette.Input, palette.Ink);
        if (control is CheckBox checkBox)
        {
            checkBox.BackColor = insideCard ? palette.Card : palette.Surface;
            checkBox.ForeColor = palette.Ink;
        }
        if (control is Button button)
        {
            button.BackColor = button == runButton ? palette.Accent : button is ChoiceButton ? palette.Input : palette.Button;
            button.ForeColor = palette.Ink;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = button == runButton ? palette.Accent : button is ChoiceButton ? palette.Input : palette.Button;
            button.FlatAppearance.MouseDownBackColor = button == runButton ? palette.Accent : button is ChoiceButton ? palette.Input : palette.Button;
            button.UseVisualStyleBackColor = false;
            button.TabStop = false;
        }
        if (control is ProgressBar progress)
        {
            progress.BackColor = palette.Input;
            progress.ForeColor = palette.Accent;
        }

        foreach (Control child in control.Controls) ApplyThemeTo(child, insideCard, palette);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (cancellation is null) return;
        var choice = MessageBox.Show("an operation is still running. cancel and exit?", "win fs tools", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (choice == DialogResult.Yes) cancellation.Cancel();
        else e.Cancel = true;
    }

    private void AppendLog(string text)
    {
        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
    }

    private Panel MakeCard()
    {
        var card = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), BorderStyle = BorderStyle.FixedSingle, Tag = "card" };
        cards.Add(card);
        return card;
    }

    private static Label MakeLabel(string text) => new() { Text = text, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Empty, Tag = "muted", Margin = new Padding(0) };

    private static void ConfigureTextBox(TextBox box)
    {
        box.Dock = DockStyle.Fill;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Margin = new Padding(0, 3, 10, 3);
    }

    private static void ConfigureChoiceButton(ChoiceButton button)
    {
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 3, 0, 3);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.TabStop = false;
        button.UseVisualStyleBackColor = false;
    }

    private static void ConfigureButton(Button button, string text)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Font = new Font("Segoe UI Semibold", 9.5f);
        button.Cursor = Cursors.Hand;
        button.TabStop = false;
        button.UseVisualStyleBackColor = false;
    }

    private Button MakeBrowseButton(string text, TextBox target, bool output)
    {
        var button = new NoFocusButton { Text = text, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Font = new Font("Segoe UI Semibold", 9.5f);
        button.Cursor = Cursors.Hand;
        button.Click += (_, _) =>
        {
            using var picker = new PathPickerForm(target.Text, allowFiles: !output, darkMode);
            if (picker.ShowDialog(this) == DialogResult.OK) target.Text = picker.SelectedPath;
        };
        return button;
    }

    private static string GetOperationName(OperationKind kind) => kind switch
    {
        OperationKind.DeleteDuplicates => "delete duplicates",
        OperationKind.CopyByExtension => "copy by extension",
        OperationKind.MoveByExtension => "move by extension",
        OperationKind.DeleteByExtension => "delete by extension",
        OperationKind.BulkCompress => "bulk compress",
        OperationKind.BulkUncompress => "bulk un-compress",
        OperationKind.SortFiles => "sort files",
        OperationKind.CreateChecksums => "create checksums",
        _ => "operation"
    };

    private static void ShowError(string message) => MessageBox.Show(message, "win fs tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
