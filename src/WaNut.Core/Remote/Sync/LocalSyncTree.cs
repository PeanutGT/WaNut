namespace WaNut.Core.Remote.Sync;

/// <summary>A server folder on this PC.</summary>
public sealed class LocalSyncTree(string root) : ISyncTree
{
    public string Root { get; } = Path.GetFullPath(root);

    public Task<IReadOnlyList<SyncEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<SyncEntry>();
        if (Directory.Exists(Root))
        {
            Walk(new DirectoryInfo(Root), string.Empty, entries, cancellationToken);
        }

        return Task.FromResult<IReadOnlyList<SyncEntry>>(entries);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new FileStream(FullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));

    public async Task WriteAsync(string path, Stream content, DateTime modifiedUtc, CancellationToken cancellationToken = default)
    {
        var full = FullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + ".wanut-part";
        try
        {
            await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            File.SetLastWriteTimeUtc(temp, modifiedUtc);
            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(FullPath(path));
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = FullPath(path);
        File.SetAttributes(full, FileAttributes.Normal);
        File.Delete(full);
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        Directory.Delete(FullPath(path), recursive: false);
        return Task.CompletedTask;
    }

    /// <summary>The full path of <paramref name="path"/>, refusing anything that would land outside <see cref="Root"/>.</summary>
    private string FullPath(string path)
    {
        var full = Path.GetFullPath(Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSlash = Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSlash, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{path}' is outside the server folder.", nameof(path));
        }

        return full;
    }

    private static void Walk(DirectoryInfo directory, string prefix, List<SyncEntry> entries, CancellationToken cancellationToken)
    {
        foreach (var info in directory.EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (info.LinkTarget is not null)
            {
                continue;
            }

            var path = prefix + info.Name;
            if (info is DirectoryInfo sub)
            {
                entries.Add(new SyncEntry(path, true, 0, sub.LastWriteTimeUtc));
                Walk(sub, path + "/", entries, cancellationToken);
            }
            else if (info is FileInfo file)
            {
                entries.Add(new SyncEntry(path, false, file.Length, file.LastWriteTimeUtc));
            }
        }
    }
}
