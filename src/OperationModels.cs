namespace WinFsTools;

public enum OperationKind
{
    DeleteDuplicates,
    CopyByExtension,
    MoveByExtension,
    DeleteByExtension,
    BulkCompress,
    BulkUncompress,
    SortFiles,
    CreateChecksums
}

public enum SortMode
{
    Extension,
    ModifiedMonth,
    SizeBand
}

public sealed class OperationOptions
{
    public required OperationKind Kind { get; init; }
    public required string InputPath { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public bool Recursive { get; init; }
    public bool BackupBeforeDelete { get; init; }
    public bool DeleteParentIfIdentical { get; init; }
    public bool MoveInsteadOfCopy { get; init; }
    public SortMode SortMode { get; init; } = SortMode.Extension;
    public IReadOnlySet<string> Extensions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record OperationProgress(int Current, int Total, string Status, string? CurrentPath = null)
{
    public int Percent => Total <= 0 ? 0 : Math.Clamp((int)Math.Round(Current * 100d / Total), 0, 100);
}

public sealed class OperationResult
{
    public int Processed { get; set; }
    public int Skipped { get; set; }
    public List<string> Warnings { get; } = [];
}
