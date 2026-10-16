using WaNut.Core.Backups;

namespace WaNut.Core.Instances;

/// <summary>
/// Copies a stopped server into a new folder under a new name: world, settings, mods and plugins, but not its
/// backups, logs or crash reports. The copy shows up in the list only once it is complete.
/// </summary>
public sealed class InstanceCopier(InstanceStore store, TimeProvider clock)
{
    // Top-level folders that belong to the original's history, not to the server itself.
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        BackupStore.FolderName, "logs", "crash-reports",
    };

    private static readonly EnumerationOptions TopLevel = new()
    {
        // A link could point anywhere on the PC; it is left out rather than followed or copied.
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
    };

    private static readonly EnumerationOptions AllFiles = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
    };

    /// <summary>Copies <paramref name="source"/> as a new server called <paramref name="newName"/> and returns it.</summary>
    public async Task<InstanceMetadata> CopyAsync(
        InstanceMetadata source, string newName, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var sourceDir = store.InstanceDir(source.Id);
        if (!File.Exists(Path.Combine(sourceDir, InstanceMetadata.FileName)))
        {
            throw new DirectoryNotFoundException(sourceDir);
        }

        var id = store.CreateFolder(newName);
        var targetDir = store.InstanceDir(id);
        try
        {
            await Task.Run(() => CopyFiles(sourceDir, targetDir, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
            var copy = source with { Id = id, Name = newName.Trim(), CreatedAt = clock.GetUtcNow() };

            // Written last: until instance.json exists, the half-made folder is not a server.
            store.Save(copy);
            progress?.Report(1);
            return copy;
        }
        catch
        {
            TryDelete(targetDir);
            throw;
        }
    }

    private static void CopyFiles(string sourceDir, string targetDir, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var files = new List<FileInfo>();
        foreach (var entry in new DirectoryInfo(sourceDir).EnumerateFileSystemInfos("*", TopLevel))
        {
            if (entry is DirectoryInfo dir)
            {
                if (!SkippedFolders.Contains(dir.Name) && !dir.Name.StartsWith(".wanut-", StringComparison.OrdinalIgnoreCase))
                {
                    files.AddRange(dir.EnumerateFiles("*", AllFiles));
                }
            }
            else if (entry is FileInfo file && !file.Name.Equals(InstanceMetadata.FileName, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(file);
            }
        }

        // Minecraft holds session.lock open while running and writes a new one on every start.
        files.RemoveAll(f => f.Name.Equals("session.lock", StringComparison.OrdinalIgnoreCase));

        var total = Math.Max(1, files.Sum(f => f.Length));
        long done = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(targetDir, Path.GetRelativePath(sourceDir, file.FullName));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            file.CopyTo(target);
            done += file.Length;
            progress?.Report(Math.Min(0.99, (double)done / total));
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An incomplete copy without instance.json never shows in the list; it can be removed by hand.
        }
    }
}
