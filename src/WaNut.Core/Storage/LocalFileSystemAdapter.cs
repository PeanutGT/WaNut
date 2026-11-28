using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace WaNut.Core.Storage;

/// <summary>A server folder on this PC.</summary>
public sealed class LocalFileSystemAdapter : IServerFileSystem
{
    private const int BufferSize = 81920;
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public LocalFileSystemAdapter(string root) => Root = Path.GetFullPath(root);

    public string Root { get; }

    public bool IsRemote => false;

    public string WorkspaceRoot => Root;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var full = FullPath(relativePath);
        return Task.FromResult(File.Exists(full) || Directory.Exists(full));
    }

    public Task<IReadOnlyList<ServerFileNode>> ListDirectoryAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        var relative = ServerPath.Normalize(relativePath);
        var folder = new DirectoryInfo(FullPath(relative));
        if (!folder.Exists)
        {
            throw new DirectoryNotFoundException($"'{relative}' doesn't exist in the server folder.");
        }

        var nodes = new List<ServerFileNode>();
        Walk(folder, relative, recursive, nodes, cancellationToken);
        return Task.FromResult<IReadOnlyList<ServerFileNode>>(nodes);
    }

    public async Task<string> ReadAllTextAsync(string relativePath, CancellationToken cancellationToken = default) =>
        await File.ReadAllTextAsync(FullPath(relativePath), cancellationToken).ConfigureAwait(false);

    public async Task WriteAllTextAsync(string relativePath, string content, CancellationToken cancellationToken = default)
    {
        var full = FullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await WriteAtomicallyAsync(full, async temp =>
            await File.WriteAllTextAsync(temp, content, Utf8NoBom, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    public async Task UploadFileAsync(string localPath, string relativePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var full = FullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await WriteAtomicallyAsync(full, temp => CopyFileAsync(localPath, temp, progress, cancellationToken)).ConfigureAwait(false);
    }

    public async Task DownloadFileAsync(string relativePath, string localPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var target = Path.GetFullPath(localPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await WriteAtomicallyAsync(target, temp => CopyFileAsync(FullPath(relativePath), temp, progress, cancellationToken)).ConfigureAwait(false);
    }

    public Task DeleteBatchAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default)
    {
        // Check every path before deleting anything, so one bad path doesn't leave a half-done clean-up.
        var targets = relativePaths.Select(ServerPath.Normalize).Distinct(StringComparer.Ordinal).ToList();
        if (targets.Contains(string.Empty))
        {
            throw new ArgumentException("The server folder itself can't be deleted.", nameof(relativePaths));
        }

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var full = FullPath(target);
            if (Directory.Exists(full))
            {
                ClearReadOnly(new DirectoryInfo(full));
                Directory.Delete(full, recursive: true);
            }
            else if (File.Exists(full))
            {
                File.SetAttributes(full, FileAttributes.Normal);
                File.Delete(full);
            }
        }

        return Task.CompletedTask;
    }

    public async Task<string> CreateArchiveAsync(IEnumerable<string> targetRelativePaths, string outputArchiveRelativePath, CancellationToken cancellationToken = default)
    {
        var output = ServerPath.Normalize(outputArchiveRelativePath);
        var outputFull = FullPath(output);
        var targets = targetRelativePaths.Select(ServerPath.Normalize).Distinct(StringComparer.Ordinal).ToList();
        foreach (var target in targets)
        {
            var full = FullPath(target);
            if (target.Length == 0 || !(File.Exists(full) || Directory.Exists(full)))
            {
                throw new FileNotFoundException($"'{target}' can't be archived: it doesn't exist or is the whole server folder.");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputFull)!);
        await WriteAtomicallyAsync(outputFull, async temp =>
        {
            await using var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            await using var gzip = new GZipStream(file, CompressionLevel.Optimal);
            await using var tar = new TarWriter(gzip, TarEntryFormat.Gnu, leaveOpen: false);
            foreach (var target in targets)
            {
                await AddToTarAsync(tar, target, outputFull, cancellationToken).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
        return output;
    }

    public Task MoveAsync(string fromRelativePath, string toRelativePath, CancellationToken cancellationToken = default)
    {
        var from = FullPath(FileRelative(fromRelativePath));
        var to = FullPath(FileRelative(toRelativePath));
        if (!File.Exists(from))
        {
            throw new FileNotFoundException($"'{fromRelativePath}' doesn't exist in the server folder.", fromRelativePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to, overwrite: false);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<string>> HashSha512Async(IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        var hashes = new List<string>(relativePaths.Count);
        foreach (var path in relativePaths)
        {
            await using var stream = new FileStream(FullPath(FileRelative(path)), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, useAsync: true);
            hashes.Add(Convert.ToHexStringLower(await System.Security.Cryptography.SHA512.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)));
        }

        return hashes;
    }

    private static string FileRelative(string relativePath)
    {
        var relative = ServerPath.Normalize(relativePath);
        return relative.Length > 0 ? relative : throw new ArgumentException("A file path is needed, not the server folder.", nameof(relativePath));
    }

    private async Task AddToTarAsync(TarWriter tar, string relative, string skipFull, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var full = FullPath(relative);
        if (Directory.Exists(full))
        {
            foreach (var child in new DirectoryInfo(full).EnumerateFileSystemInfos().Where(i => !i.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            {
                await AddToTarAsync(tar, ServerPath.Combine(relative, child.Name), skipFull, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (!string.Equals(full, skipFull, StringComparison.Ordinal))
        {
            await tar.WriteEntryAsync(full, relative, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteAtomicallyAsync(string target, Func<string, Task> write)
    {
        var temp = target + ".wanut-part";
        try
        {
            await write(temp).ConfigureAwait(false);
            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }

    private static async Task CopyFileAsync(string source, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        await using var read = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, useAsync: true);
        await using var write = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        var buffer = new byte[BufferSize];
        long done = 0;
        int count;
        while ((count = await read.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await write.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            done += count;
            progress?.Report(read.Length == 0 ? 1 : (double)done / read.Length);
        }

        progress?.Report(1);
    }

    private static void Walk(DirectoryInfo folder, string relative, bool recursive, List<ServerFileNode> nodes, CancellationToken cancellationToken)
    {
        var children = folder.EnumerateFileSystemInfos()
            .Where(i => !i.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .OrderByDescending(i => i is DirectoryInfo)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ServerPath.Combine(relative, child.Name);
            var isDirectory = child is DirectoryInfo;
            nodes.Add(new ServerFileNode(child.Name, path, isDirectory, child is FileInfo file ? file.Length : 0,
                child.LastWriteTimeUtc, McFileClassifier.Classify(path, isDirectory)));
            if (recursive && child is DirectoryInfo directory)
            {
                Walk(directory, path, recursive, nodes, cancellationToken);
            }
        }
    }

    private static void ClearReadOnly(DirectoryInfo folder)
    {
        foreach (var file in folder.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (file.IsReadOnly)
            {
                file.IsReadOnly = false;
            }
        }
    }

    /// <summary>The full path of <paramref name="relativePath"/>, refusing anything that would land outside <see cref="Root"/>.</summary>
    private string FullPath(string relativePath)
    {
        var relative = ServerPath.Normalize(relativePath);
        return relative.Length == 0
            ? Root
            : Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
    }
}
