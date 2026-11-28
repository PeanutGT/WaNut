using System.Text;
using Renci.SshNet.Common;
using WaNut.Core.Remote.Sync;
using WaNut.Core.Storage;

namespace WaNut.Core.Remote.Files;

/// <summary>
/// A server folder on the user's VPS. Listing, deleting and packing run as one SSH command each (thousands of region
/// files would take thousands of SFTP round trips); reading and writing single files go over SFTP. Opened with
/// <see cref="OpenAsync"/>, which resolves the folder's real path and refuses system and home folders.
/// </summary>
public sealed class RemoteVpsAdapter : IServerFileSystem
{
    private const int BufferSize = 81920;
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly VpsSession _session;
    private readonly bool _ownsSession;

    private RemoteVpsAdapter(VpsSession session, string root, bool ownsSession)
    {
        _session = session;
        Root = root;
        _ownsSession = ownsSession;
    }

    public bool IsRemote => true;

    /// <summary>The folder's real absolute path on the VPS.</summary>
    public string WorkspaceRoot => Root;

    public string Root { get; }

    /// <param name="root">The server folder: absolute, or relative to the SSH user's home (such as <see cref="SftpSyncTree.ServerRoot"/>).</param>
    /// <param name="ownsSession">Whether disposing the adapter closes <paramref name="session"/>.</param>
    /// <exception cref="DirectoryNotFoundException">The folder doesn't exist on the VPS.</exception>
    /// <exception cref="WorkspaceRootException">The folder is a system or home folder.</exception>
    public static async Task<RemoteVpsAdapter> OpenAsync(VpsSession session, string root, bool ownsSession = false, CancellationToken cancellationToken = default)
    {
        var result = await session.RunAsync(RemoteFileCommands.Resolve(root), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.ExitStatus == RemoteFileCommands.NoFolderExit)
        {
            throw new DirectoryNotFoundException($"'{root}' doesn't exist on the VPS.");
        }

        var parts = Succeeded(result, "Finding the server folder").Output.Split('\0');
        if (parts.Length != 2)
        {
            throw new FormatException("Unexpected answer from the VPS while finding the server folder.");
        }

        WorkspaceRootGuard.Check(parts[1], parts[0]);
        return new RemoteVpsAdapter(session, parts[1], ownsSession);
    }

    public ValueTask DisposeAsync() => _ownsSession ? _session.DisposeAsync() : ValueTask.CompletedTask;

    public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var full = FullPath(relativePath);
        return _session.SftpAsync(sftp => sftp.ExistsAsync(full, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<ServerFileNode>> ListDirectoryAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        var relative = ServerPath.Normalize(relativePath);
        var full = FullPath(relative);
        var isFolder = await _session.SftpAsync(async sftp =>
        {
            try
            {
                return (await sftp.GetAttributesAsync(full, cancellationToken).ConfigureAwait(false)).IsDirectory;
            }
            catch (SftpPathNotFoundException)
            {
                return false;
            }
        }, cancellationToken).ConfigureAwait(false);
        if (!isFolder)
        {
            throw new DirectoryNotFoundException($"'{relative}' doesn't exist in the server folder.");
        }

        var listed = await _session.RunAsync(RemoteListing.Command(full, recursive), cancellationToken: cancellationToken).ConfigureAwait(false);
        var entries = listed.ExitStatus == 0
            ? RemoteListing.Parse(listed.Output)
            : await ListOverSftpAsync(full, recursive, cancellationToken).ConfigureAwait(false);
        return ServerTree.Sort(entries.Select(e =>
        {
            var path = ServerPath.Combine(relative, e.Path);
            return new ServerFileNode(e.Path[(e.Path.LastIndexOf('/') + 1)..], path, e.IsDirectory, e.Size, e.ModifiedUtc, McFileClassifier.Classify(path, e.IsDirectory));
        }));
    }

    public Task<string> ReadAllTextAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var full = FullPath(relativePath);
        return _session.SftpAsync(async sftp =>
        {
            try
            {
                await using var stream = await sftp.OpenAsync(full, FileMode.Open, FileAccess.Read, cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SftpPathNotFoundException e)
            {
                throw new FileNotFoundException($"'{relativePath}' doesn't exist in the server folder.", relativePath, e);
            }
        }, cancellationToken);
    }

    public Task WriteAllTextAsync(string relativePath, string content, CancellationToken cancellationToken = default)
    {
        var relative = FileRelative(relativePath);
        var bytes = Utf8NoBom.GetBytes(content);
        return _session.SftpAsync(async sftp =>
        {
            // SftpSyncTree writes under a temporary name, checks the size the VPS kept, then renames over the file.
            await new SftpSyncTree(sftp, Root).WriteAsync(relative, new MemoryStream(bytes), DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task UploadFileAsync(string localPath, string relativePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var relative = FileRelative(relativePath);
        return _session.TransferAsync(async sftp =>
        {
            await using var file = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, useAsync: true);
            var length = file.Length;
            var counting = new CountingStream(file, n => progress?.Report(length == 0 ? 1 : (double)n / length));
            await new SftpSyncTree(sftp, Root).WriteAsync(relative, counting, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
            progress?.Report(1);
            return true;
        }, cancellationToken);
    }

    public Task DownloadFileAsync(string relativePath, string localPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var full = FullPath(FileRelative(relativePath));
        var target = Path.GetFullPath(localPath);
        return _session.TransferAsync(async sftp =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temp = target + ".wanut-part";
            try
            {
                long length;
                await using (var read = await OpenOrNotFoundAsync(sftp, full, relativePath, cancellationToken).ConfigureAwait(false))
                await using (var write = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
                {
                    length = read.Length;
                    var buffer = new byte[BufferSize];
                    long done = 0;
                    int count;
                    while ((count = await read.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await write.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                        done += count;
                        progress?.Report(length == 0 ? 1 : (double)done / length);
                    }
                }

                File.Move(temp, target, overwrite: true);
                progress?.Report(1);
                return true;
            }
            catch
            {
                File.Delete(temp);
                throw;
            }
        }, cancellationToken);
    }

    /// <exception cref="WorkspaceRootException">The folder no longer looks like a server folder, or became another folder.</exception>
    /// <exception cref="UnauthorizedAccessException">A path goes through a link out of the server folder. Nothing was deleted.</exception>
    public async Task DeleteBatchAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default)
    {
        // Check every path before sending anything, so one bad path doesn't leave a half-done clean-up.
        var targets = relativePaths.Select(ServerPath.Normalize).Distinct(StringComparer.Ordinal).ToList();
        if (targets.Contains(string.Empty))
        {
            throw new ArgumentException("The server folder itself can't be deleted.", nameof(relativePaths));
        }

        if (targets.Count == 0)
        {
            return;
        }

        var input = string.Concat(targets.Select(t => t + "\0"));
        var result = await _session.RunAsync(RemoteFileCommands.Delete(Root), input, cancellationToken).ConfigureAwait(false);
        if (result.Error.Contains(RemoteFileCommands.OutsideMarker, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("A path to delete goes through a link out of the server folder. Nothing in that batch was deleted.");
        }

        ThrowForGuard(result);
        Succeeded(result, "Deleting");
    }

    public async Task<string> CreateArchiveAsync(IEnumerable<string> targetRelativePaths, string outputArchiveRelativePath, CancellationToken cancellationToken = default)
    {
        var output = FileRelative(outputArchiveRelativePath);
        var targets = targetRelativePaths.Select(ServerPath.Normalize).Distinct(StringComparer.Ordinal).ToList();
        if (targets.Count == 0 || targets.Contains(string.Empty))
        {
            throw new FileNotFoundException("Choose the files and folders to archive; the whole server folder can't be archived into itself.");
        }

        var temp = $"{TarCommands.StageFolder}-archive-{Guid.NewGuid():N}.tar.gz";
        var result = await _session.RunAsync(RemoteFileCommands.Archive(Root, targets, output, temp), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.ExitStatus == RemoteFileCommands.MissingExit)
        {
            throw new FileNotFoundException("A file or folder to archive doesn't exist in the server folder.");
        }

        ThrowForGuard(result);
        Succeeded(result, "Packing");
        return output;
    }

    /// <exception cref="UnauthorizedAccessException">A path goes through a link out of the server folder. Nothing was moved.</exception>
    public async Task MoveAsync(string fromRelativePath, string toRelativePath, CancellationToken cancellationToken = default)
    {
        var from = FileRelative(fromRelativePath);
        var to = FileRelative(toRelativePath);
        var result = await _session.RunAsync(RemoteFileCommands.Move(Root, from, to), cancellationToken: cancellationToken).ConfigureAwait(false);
        switch (result.ExitStatus)
        {
            case RemoteFileCommands.NotFoundExit:
                throw new FileNotFoundException($"'{from}' doesn't exist in the server folder.", from);
            case RemoteFileCommands.ExistsExit:
                throw new IOException($"'{to}' already exists in the server folder.");
            case RemoteFileCommands.OutsideExit:
                throw new UnauthorizedAccessException("A path to move goes through a link out of the server folder. Nothing was moved.");
        }

        ThrowForGuard(result);
        Succeeded(result, "Moving");
    }

    public async Task<IReadOnlyList<string>> HashSha512Async(IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        var paths = relativePaths.Select(FileRelative).ToList();
        if (paths.Count == 0)
        {
            return [];
        }

        var result = await _session.RunAsync(RemoteFileCommands.Hash(Root, paths), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.ExitStatus == RemoteFileCommands.NoCommandExit)
        {
            return await HashByDownloadAsync(paths, cancellationToken).ConfigureAwait(false);
        }

        if (result.ExitStatus == RemoteFileCommands.NotFoundExit)
        {
            throw new FileNotFoundException("A file to check doesn't exist in the server folder.");
        }

        ThrowForGuard(result);
        var lines = Succeeded(result, "Checking files").Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length != paths.Count || lines.Any(l => l.Length != 128 || !l.All(char.IsAsciiHexDigitLower)))
        {
            throw new FormatException("Unexpected answer from the VPS while checking files.");
        }

        return lines;
    }

    /// <summary>A VPS without sha512sum (rare: it is part of coreutils): each file is read over SFTP and hashed here.</summary>
    private async Task<IReadOnlyList<string>> HashByDownloadAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var hashes = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            var full = FullPath(path);
            hashes.Add(await _session.TransferAsync(async sftp =>
            {
                await using var read = await OpenOrNotFoundAsync(sftp, full, path, cancellationToken).ConfigureAwait(false);
                return Convert.ToHexStringLower(await System.Security.Cryptography.SHA512.HashDataAsync(read, cancellationToken).ConfigureAwait(false));
            }, cancellationToken).ConfigureAwait(false));
        }

        return hashes;
    }

    /// <summary>The same listing without GNU find (BusyBox, some minimal images): slower, folder by folder.</summary>
    private Task<IReadOnlyList<SyncEntry>> ListOverSftpAsync(string full, bool recursive, CancellationToken cancellationToken) =>
        _session.SftpAsync<IReadOnlyList<SyncEntry>>(async sftp =>
        {
            if (recursive)
            {
                return await new SftpSyncTree(sftp, full).ListAsync(cancellationToken).ConfigureAwait(false);
            }

            var entries = new List<SyncEntry>();
            await foreach (var file in sftp.ListDirectoryAsync(full, cancellationToken).ConfigureAwait(false))
            {
                if (file.Name is not "." and not ".." && !file.Name.Contains('\\') && (file.IsDirectory || file.IsRegularFile))
                {
                    entries.Add(new SyncEntry(file.Name, file.IsDirectory, file.IsDirectory ? 0 : file.Length, file.LastWriteTimeUtc));
                }
            }

            return entries;
        }, cancellationToken);

    private static async Task<Stream> OpenOrNotFoundAsync(Renci.SshNet.SftpClient sftp, string full, string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            return await sftp.OpenAsync(full, FileMode.Open, FileAccess.Read, cancellationToken).ConfigureAwait(false);
        }
        catch (SftpPathNotFoundException e)
        {
            throw new FileNotFoundException($"'{relativePath}' doesn't exist in the server folder.", relativePath, e);
        }
    }

    private void ThrowForGuard(RemoteCommandResult result)
    {
        switch (result.ExitStatus)
        {
            case RemoteFileCommands.NoFolderExit:
                throw new DirectoryNotFoundException($"'{Root}' no longer exists on the VPS.");
            case RemoteFileCommands.MissingExit:
                throw new WorkspaceRootException(Root, WorkspaceRootProblem.NoServerFiles);
            case RemoteFileCommands.MovedExit:
                throw new WorkspaceRootException(Root, WorkspaceRootProblem.Moved);
        }
    }

    private static RemoteCommandResult Succeeded(RemoteCommandResult result, string what) =>
        result.ExitStatus == 0
            ? result
            : throw new InvalidOperationException($"{what} on the VPS failed ({result.ExitStatus}): {result.Error.Trim()}");

    private static string FileRelative(string relativePath)
    {
        var relative = ServerPath.Normalize(relativePath);
        return relative.Length > 0 ? relative : throw new ArgumentException("A file path is needed, not the server folder.", nameof(relativePath));
    }

    private string FullPath(string relativePath)
    {
        var relative = ServerPath.Normalize(relativePath);
        return relative.Length == 0 ? Root : $"{Root}/{relative}";
    }
}
