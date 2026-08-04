using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;

namespace WinFsTools;

public sealed class FileOperationEngine
{
    public Task<OperationResult> RunAsync(
        OperationOptions options,
        IProgress<OperationProgress> progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => RunOnWorkerAsync(options, new ThrottledProgress(progress), cancellationToken), cancellationToken);
    }

    private async Task<OperationResult> RunOnWorkerAsync(
        OperationOptions options,
        IProgress<OperationProgress> progress,
        CancellationToken cancellationToken)
    {
        return options.Kind switch
        {
            OperationKind.DeleteDuplicates => await DeleteDuplicatesAsync(options, progress, cancellationToken),
            OperationKind.CopyByExtension => await TransferByExtensionAsync(options, progress, cancellationToken, move: false),
            OperationKind.MoveByExtension => await TransferByExtensionAsync(options, progress, cancellationToken, move: true),
            OperationKind.DeleteByExtension => await DeleteByExtensionAsync(options, progress, cancellationToken),
            OperationKind.BulkCompress => await BulkCompressAsync(options, progress, cancellationToken),
            OperationKind.BulkUncompress => await BulkUncompressAsync(options, progress, cancellationToken),
            OperationKind.SortFiles => await SortFilesAsync(options, progress, cancellationToken),
            OperationKind.CreateChecksums => await CreateChecksumsAsync(options, progress, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Kind))
        };
    }

    private async Task<OperationResult> DeleteDuplicatesAsync(
        OperationOptions options,
        IProgress<OperationProgress> progress,
        CancellationToken cancellationToken)
    {
        var files = DiscoverFiles(options.InputPath, options.Recursive);
        var candidates = files.GroupBy(file => new FileInfo(file).Length).Where(group => group.Count() > 1).SelectMany(group => group).ToList();
        var hashValues = new string?[candidates.Count];
        var hashed = 0;
        var hashOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 4)
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, candidates.Count), hashOptions, async (index, token) =>
        {
            var file = candidates[index];
            try
            {
                hashValues[index] = Convert.ToHexString(await HashFileAsync(file, token));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                progress.Report(new OperationProgress(0, 0, $"could not read {Path.GetFileName(file)}", file));
            }

            progress.Report(new OperationProgress(Interlocked.Increment(ref hashed), candidates.Count, "checking duplicate files", file));
        });

        var hashes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < candidates.Count; index++)
        {
            var hash = hashValues[index];
            if (hash is null) continue;
            if (!hashes.TryGetValue(hash, out var matching))
            {
                matching = [];
                hashes.Add(hash, matching);
            }

            matching.Add(candidates[index]);
        }

        var duplicates = hashes.Values.Where(group => group.Count > 1).SelectMany(group => group.Skip(1)).ToList();
        var duplicateSet = duplicates.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parentFolders = options.DeleteParentIfIdentical
            ? FindDuplicateOnlyDirectories(options.InputPath, files, duplicateSet)
            : [];
        var parentSet = parentFolders.Select(folder => folder.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var coveredDuplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in duplicates)
        {
            var parent = FindCoveringDirectory(file, parentSet);
            if (parent is null) continue;
            coveredDuplicates.Add(file);
        }
        var individualDuplicates = duplicates.Where(file => !coveredDuplicates.Contains(file)).ToList();
        var result = new OperationResult();
        var processed = 0;
        var total = Math.Max(duplicates.Count, 1);
        var backupRoot = string.Empty;
        var backupReady = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var backupReadyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (options.BackupBeforeDelete)
        {
            backupRoot = RequireOutputDirectory(options.OutputPath);
            foreach (var file in duplicates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var destination = BuildRelativeDestination(options.InputPath, backupRoot, file);
                    CopyExact(file, destination);
                    backupReady.Add(file);
                    var parent = FindCoveringDirectory(file, parentSet);
                    if (parent is not null) backupReadyCounts[parent] = backupReadyCounts.GetValueOrDefault(parent) + 1;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    result.Warnings.Add($"could not back up {file}: {exception.Message.ToLowerInvariant()}");
                }
            }
        }

        foreach (var folder in parentFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filesReady = !options.BackupBeforeDelete || folder.FileCount == backupReadyCounts.GetValueOrDefault(folder.Path);
            try
            {
                if (!filesReady)
                {
                    result.Warnings.Add($"could not delete duplicate-only folder {folder.Path}: backup incomplete");
                }
                else
                {
                    Directory.Delete(folder.Path, recursive: true);
                    result.Processed += folder.FileCount;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                result.Warnings.Add($"could not delete duplicate-only folder {folder.Path}: {exception.Message.ToLowerInvariant()}");
            }

            processed += folder.FileCount;
            progress.Report(new OperationProgress(processed, total, "deleting duplicate-only folders", folder.Path));
        }

        foreach (var file in individualDuplicates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!options.BackupBeforeDelete || backupReady.Contains(file))
                {
                    File.Delete(file);
                    result.Processed++;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                result.Warnings.Add($"could not delete {file}: {exception.Message.ToLowerInvariant()}");
            }

            processed++;
            progress.Report(new OperationProgress(processed, total, "deleting duplicate files", file));
        }

        result.Skipped = Math.Max(0, files.Count - result.Processed);
        return result;
    }

    private Task<OperationResult> TransferByExtensionAsync(
        OperationOptions options,
        IProgress<OperationProgress> progress,
        CancellationToken cancellationToken,
        bool move)
    {
        var files = DiscoverFiles(options.InputPath, options.Recursive).Where(file => options.Extensions.Contains(Path.GetExtension(file))).ToList();
        var destinationRoot = RequireOutputDirectory(options.OutputPath);
        var result = new OperationResult { Processed = 0, Skipped = files.Count };
        var processed = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var destination = BuildRelativeDestination(options.InputPath, destinationRoot, file);
                if (!AreSamePath(file, destination))
                {
                    CopyExact(file, destination);
                    if (move) File.Delete(file);
                    result.Processed++;
                    result.Skipped--;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                result.Warnings.Add($"could not copy {file}: {exception.Message.ToLowerInvariant()}");
            }

            progress.Report(new OperationProgress(++processed, Math.Max(files.Count, 1), move ? "moving matching files" : "copying matching files", file));
        }

        return Task.FromResult(result);
    }

    private Task<OperationResult> DeleteByExtensionAsync(
        OperationOptions options,
        IProgress<OperationProgress> progress,
        CancellationToken cancellationToken)
    {
        var files = DiscoverFiles(options.InputPath, options.Recursive).Where(file => options.Extensions.Contains(Path.GetExtension(file))).ToList();
        var result = new OperationResult { Processed = 0, Skipped = files.Count };
        var processed = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Delete(file);
                result.Processed++;
                result.Skipped--;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                result.Warnings.Add($"could not delete {file}: {exception.Message.ToLowerInvariant()}");
            }

            progress.Report(new OperationProgress(++processed, Math.Max(files.Count, 1), "deleting matching files", file));
        }

        return Task.FromResult(result);
    }

    private async Task<OperationResult> BulkCompressAsync(
        OperationOptions options,
        IProgress<OperationProgress> progress,
        CancellationToken cancellationToken)
    {
        var files = DiscoverFiles(options.InputPath, options.Recursive);
        var output = ResolveArchivePath(options.InputPath, options.OutputPath);
        var parent = Path.GetDirectoryName(output);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

        var processed = 0;
        await using var stream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 64, useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryName = BuildArchiveEntryName(options.InputPath, file);
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 64, useAsync: true);
            await using var target = entry.Open();
            await source.CopyToAsync(target, 1024 * 64, cancellationToken);
            progress.Report(new OperationProgress(++processed, Math.Max(files.Count, 1), "compressing files", file));
        }

        return new OperationResult { Processed = processed, Skipped = files.Count - processed };
    }

    private async Task<OperationResult> BulkUncompressAsync(
        OperationOptions options,
        IProgress<OperationProgress> progress,
        CancellationToken cancellationToken)
    {
        var archives = DiscoverArchives(options.InputPath, options.Recursive);
        var outputRoot = RequireOutputDirectory(options.OutputPath);
        var totalEntries = 0;
        foreach (var archivePath in archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var archive = ZipFile.OpenRead(archivePath);
            totalEntries += archive.Entries.Count(entry => !string.IsNullOrEmpty(entry.Name));
        }

        var result = new OperationResult();
        var processed = 0;
        foreach (var archivePath in archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = GetUniqueDirectory(Path.Combine(outputRoot, Path.GetFileNameWithoutExtension(archivePath)));
            Directory.CreateDirectory(destination);
            try
            {
                using var archive = ZipFile.OpenRead(archivePath);
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        var directory = GetSafeArchivePath(destination, entry.FullName);
                        Directory.CreateDirectory(directory);
                        continue;
                    }

                    var filePath = GetSafeArchivePath(destination, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                    await using var source = entry.Open();
                    await using var target = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 64, useAsync: true);
                    await source.CopyToAsync(target, 1024 * 64, cancellationToken);
                    result.Processed++;
                    processed++;
                    progress.Report(new OperationProgress(processed, Math.Max(totalEntries, 1), "un-compressing files", filePath));
                }
            }
            catch (InvalidDataException exception)
            {
                result.Warnings.Add($"could not un-compress {archivePath}: {exception.Message.ToLowerInvariant()}");
            }
            catch (IOException exception)
            {
                result.Warnings.Add($"could not un-compress {archivePath}: {exception.Message.ToLowerInvariant()}");
            }
        }

        result.Skipped = Math.Max(0, totalEntries - result.Processed);
        return result;
    }

    private Task<OperationResult> SortFilesAsync(
        OperationOptions options,
        IProgress<OperationProgress> progress,
        CancellationToken cancellationToken)
    {
        var files = DiscoverFiles(options.InputPath, options.Recursive);
        var destinationRoot = RequireOutputDirectory(options.OutputPath);
        var result = new OperationResult { Processed = 0, Skipped = files.Count };
        var processed = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var bucket = GetSortBucket(file, options.SortMode);
                var destination = BuildRelativeDestination(options.InputPath, Path.Combine(destinationRoot, bucket), file);
                if (!AreSamePath(file, destination))
                {
                    if (options.MoveInsteadOfCopy)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        destination = GetUniquePath(destination);
                        File.Move(file, destination);
                    }
                    else
                    {
                        CopyExact(file, destination);
                    }

                    result.Processed++;
                    result.Skipped--;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                result.Warnings.Add($"could not sort {file}: {exception.Message.ToLowerInvariant()}");
            }

            progress.Report(new OperationProgress(++processed, Math.Max(files.Count, 1), "sorting files", file));
        }

        return Task.FromResult(result);
    }

    private async Task<OperationResult> CreateChecksumsAsync(
        OperationOptions options,
        IProgress<OperationProgress> progress,
        CancellationToken cancellationToken)
    {
        var files = DiscoverFiles(options.InputPath, options.Recursive);
        var output = ResolveChecksumPath(options.OutputPath);
        var result = new OperationResult { Processed = 0, Skipped = files.Count };
        var processed = 0;

        await using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 16, useAsync: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var hash = Convert.ToHexString(await HashFileAsync(file, cancellationToken)).ToLowerInvariant();
                var relative = BuildArchiveEntryName(options.InputPath, file);
                await writer.WriteLineAsync($"{hash}  {relative}");
                result.Processed++;
                result.Skipped--;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                result.Warnings.Add($"could not hash {file}: {exception.Message.ToLowerInvariant()}");
            }

            progress.Report(new OperationProgress(++processed, Math.Max(files.Count, 1), "creating checksums", file));
        }

        return result;
    }

    private static List<string> DiscoverFiles(string inputPath, bool recursive)
    {
        if (File.Exists(inputPath)) return [Path.GetFullPath(inputPath)];
        if (!Directory.Exists(inputPath)) throw new DirectoryNotFoundException("input path was not found");

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.System | FileAttributes.Temporary
        };

        return Directory.EnumerateFiles(inputPath, "*", options).Select(Path.GetFullPath).ToList();
    }

    private static List<string> DiscoverArchives(string inputPath, bool recursive)
    {
        if (File.Exists(inputPath))
        {
            if (!Path.GetExtension(inputPath).Equals(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("input file is not a zip archive");
            return [Path.GetFullPath(inputPath)];
        }

        if (!Directory.Exists(inputPath)) throw new DirectoryNotFoundException("input path was not found");
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.System | FileAttributes.Temporary
        };

        return Directory.EnumerateFiles(inputPath, "*.zip", options).Select(Path.GetFullPath).ToList();
    }

    private static List<DuplicateFolder> FindDuplicateOnlyDirectories(
        string inputPath,
        IReadOnlyList<string> files,
        IReadOnlySet<string> duplicates)
    {
        if (!Directory.Exists(inputPath)) return [];
        var root = Path.GetFullPath(inputPath);
        var counts = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var current = Path.GetDirectoryName(file);
            while (current is not null && current.Length > 0 && !AreSamePath(current, root))
            {
                if (!counts.TryGetValue(current, out var count))
                {
                    count = [0, 0];
                    counts.Add(current, count);
                }

                count[0]++;
                if (duplicates.Contains(file)) count[1]++;
                current = Directory.GetParent(current)?.FullName;
            }
        }

        var candidates = counts
            .Where(pair => pair.Value[0] > 0 && pair.Value[0] == pair.Value[1])
            .Select(pair => new DuplicateFolder(pair.Key, pair.Value[0]))
            .OrderBy(folder => folder.Path.Length)
            .ToList();
        var selected = new List<DuplicateFolder>();
        foreach (var candidate in candidates)
        {
            if (selected.All(parent => !IsWithinDirectory(parent.Path, candidate.Path))) selected.Add(candidate);
        }

        return selected;
    }

    private static string? FindCoveringDirectory(string file, IReadOnlySet<string> directories)
    {
        var current = Path.GetDirectoryName(file);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (directories.Contains(current)) return current;
            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }

    private static bool IsWithinDirectory(string parent, string child)
    {
        var relative = Path.GetRelativePath(parent, child);
        return relative != "." && relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static string GetUniqueDirectory(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var parent = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(parent, $"{name} ({index})");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private static string GetSafeArchivePath(string root, string entryName)
    {
        var normalized = entryName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized)) throw new InvalidDataException("archive entry has an absolute path");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalized));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("archive entry escapes the output folder");
        return fullPath;
    }

    private static async Task<byte[]> HashFileAsync(string file, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 64, useAsync: true);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    private static string RequireOutputDirectory(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("an output path is required");
        var fullPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullPath)) throw new IOException("the output path is a file");
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    private static string BuildRelativeDestination(string inputPath, string destinationRoot, string sourceFile)
    {
        var relative = File.Exists(inputPath) ? Path.GetFileName(sourceFile) : Path.GetRelativePath(Path.GetFullPath(inputPath), sourceFile);
        var destination = Path.Combine(destinationRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        return GetUniquePath(destination);
    }

    private static string BuildArchiveEntryName(string inputPath, string sourceFile)
    {
        var relative = File.Exists(inputPath) ? Path.GetFileName(sourceFile) : Path.GetRelativePath(Path.GetFullPath(inputPath), sourceFile);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string ResolveArchivePath(string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("an output path is required");
        var fullOutput = Path.GetFullPath(outputPath);
        if (Path.GetExtension(fullOutput).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return fullOutput;
        var name = File.Exists(inputPath) ? Path.GetFileNameWithoutExtension(inputPath) : new DirectoryInfo(inputPath).Name;
        return Path.Combine(fullOutput, $"{name}.zip");
    }

    private static string ResolveChecksumPath(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("an output path is required");
        var fullOutput = Path.GetFullPath(outputPath);
        var output = Path.GetExtension(fullOutput).Equals(".sha256", StringComparison.OrdinalIgnoreCase)
            ? fullOutput
            : Path.Combine(fullOutput, "checksums.sha256");
        var parent = Path.GetDirectoryName(output);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        return GetUniquePath(output);
    }

    private static string GetSortBucket(string file, SortMode mode)
    {
        return mode switch
        {
            SortMode.Extension => string.IsNullOrWhiteSpace(Path.GetExtension(file)) ? "no extension" : Path.GetExtension(file).TrimStart('.').ToLowerInvariant(),
            SortMode.ModifiedMonth => File.GetLastWriteTime(file).ToString("yyyy-MM"),
            SortMode.SizeBand => GetSizeBand(new FileInfo(file).Length),
            _ => "other"
        };
    }

    private static string GetSizeBand(long bytes)
    {
        const long megabyte = 1024 * 1024;
        return bytes switch
        {
            < megabyte => "under 1 mb",
            < 10 * megabyte => "1 to 10 mb",
            < 100 * megabyte => "10 to 100 mb",
            _ => "over 100 mb"
        };
    }

    private static void CopyExact(string source, string destination)
    {
        if (AreSamePath(source, destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static bool AreSamePath(string first, string second) =>
        string.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private sealed record DuplicateFolder(string Path, int FileCount);

    private sealed class ThrottledProgress : IProgress<OperationProgress>
    {
        private readonly IProgress<OperationProgress> sink;
        private readonly object gate = new();
        private long lastReportTimestamp;
        private string? lastStatus;

        public ThrottledProgress(IProgress<OperationProgress> sink) => this.sink = sink;

        public void Report(OperationProgress value)
        {
            var now = Stopwatch.GetTimestamp();
            var shouldReport = false;
            lock (gate)
            {
                var statusChanged = !string.Equals(lastStatus, value.Status, StringComparison.Ordinal);
                var complete = value.Total > 0 && value.Current >= value.Total;
                var elapsed = now - lastReportTimestamp;
                shouldReport = statusChanged || complete || elapsed >= Stopwatch.Frequency / 20;
                if (shouldReport)
                {
                    lastStatus = value.Status;
                    lastReportTimestamp = now;
                }
            }

            if (shouldReport) sink.Report(value);
        }
    }
}
