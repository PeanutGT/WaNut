using System.Collections.Concurrent;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace WaNut.Core.Remote.Sync;

/// <summary>
/// A server folder on the VPS, reached over SFTP. <paramref name="root"/> is relative to the user's home. Calls for
/// different paths may run at the same time: SSH.NET matches each SFTP reply to its request.
/// </summary>
public sealed class SftpSyncTree(SftpClient sftp, string root) : ISyncTree
{
    /// <summary>Folders listed at a time; each listing is a few round trips to the VPS.</summary>
    private const int MaxParallelListings = 8;

    /// <summary>Folders known to exist on the VPS, so writing a file doesn't ask about each of its parents again.</summary>
    private readonly ConcurrentDictionary<string, byte> _knownDirectories = new(StringComparer.Ordinal);

    /// <summary>Where WaNut keeps its servers on the VPS, relative to the SSH user's home.</summary>
    public const string ServersFolder = "wanut/servers";

    public string Root { get; } = root.TrimEnd('/');

    /// <summary>The VPS folder for the server whose folder on this PC is named <paramref name="instanceId"/>.</summary>
    public static string ServerRoot(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || instanceId is "." or ".." || instanceId.IndexOfAny(['/', '\\', '\0']) >= 0)
        {
            throw new ArgumentException($"'{instanceId}' can't be used as a folder name on the VPS.", nameof(instanceId));
        }

        return $"{ServersFolder}/{instanceId}";
    }

    public async Task<IReadOnlyList<SyncEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entries = new ConcurrentBag<SyncEntry>();
        if (await sftp.ExistsAsync(Root, cancellationToken).ConfigureAwait(false))
        {
            _knownDirectories.TryAdd(Root, 0);

            // One folder level at a time, the folders of a level side by side: a server has hundreds of folders
            // (libraries alone nest deep), and listing them one by one waits on the VPS for each.
            List<(string Full, string Relative)> level = [(Root, string.Empty)];
            while (level.Count > 0)
            {
                var next = new ConcurrentBag<(string Full, string Relative)>();
                var options = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelListings, CancellationToken = cancellationToken };
                await Parallel.ForEachAsync(level, options, async (directory, token) =>
                {
                    await foreach (var file in sftp.ListDirectoryAsync(directory.Full, token).ConfigureAwait(false))
                    {
                        // Names Windows can't hold as one path part are left on the VPS.
                        if (file.Name is "." or ".." || file.Name.Contains('\\'))
                        {
                            continue;
                        }

                        var path = directory.Relative + file.Name;
                        if (file.IsDirectory)
                        {
                            var full = $"{directory.Full}/{file.Name}";
                            entries.Add(new SyncEntry(path, true, 0, file.LastWriteTimeUtc));
                            _knownDirectories.TryAdd(full, 0);
                            next.Add((full, path + "/"));
                        }
                        else if (file.IsRegularFile)
                        {
                            entries.Add(new SyncEntry(path, false, file.Length, file.LastWriteTimeUtc));
                        }
                    }
                }).ConfigureAwait(false);
                level = [.. next];
            }
        }

        return [.. entries];
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
        await sftp.OpenAsync(FullPath(path), FileMode.Open, FileAccess.Read, cancellationToken).ConfigureAwait(false);

    public async Task WriteAsync(string path, Stream content, DateTime modifiedUtc, CancellationToken cancellationToken = default)
    {
        var full = FullPath(path);
        await CreateParentsAsync(full, cancellationToken).ConfigureAwait(false);
        var temp = full + ".wanut-part";
        try
        {
            long sent = 0;
            await sftp.UploadFileAsync(new CountingStream(content, n => sent = n), temp, cancellationToken).ConfigureAwait(false);

            // A full disk or a dropped channel can leave a short file that looks fine; never let it replace the real one.
            var attributes = await sftp.GetAttributesAsync(temp, cancellationToken).ConfigureAwait(false);
            if (attributes.Size != sent)
            {
                throw new IOException($"The VPS kept fewer bytes of {path} than were sent. Is its disk full?");
            }

            // Only the changed time is sent back; SetLastWriteTimeUtc would read the attributes once more first.
            attributes.LastWriteTimeUtc = modifiedUtc;
            sftp.SetAttributes(temp, attributes);
            await ReplaceAsync(temp, full, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await sftp.DeleteFileAsync(temp, CancellationToken.None).ConfigureAwait(false);
            }
            catch (SshException)
            {
                // Nothing was written, or the connection is gone; the next sync ignores .wanut-part files anyway.
            }

            throw;
        }
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = FullPath(path);
        await CreateParentsAsync(full, cancellationToken).ConfigureAwait(false);
        if (_knownDirectories.ContainsKey(full))
        {
            return;
        }

        // FolderSync only asks for folders its listing didn't have, so try making it before asking whether it exists.
        try
        {
            await sftp.CreateDirectoryAsync(full, cancellationToken).ConfigureAwait(false);
        }
        catch (SshException)
        {
            if (!await sftp.ExistsAsync(full, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }
        }

        _knownDirectories.TryAdd(full, 0);
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default) =>
        sftp.DeleteFileAsync(FullPath(path), cancellationToken);

    public async Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = FullPath(path);
        await sftp.DeleteDirectoryAsync(full, cancellationToken).ConfigureAwait(false);
        _knownDirectories.TryRemove(full, out _);
    }

    private string FullPath(string path)
    {
        if (path.Length == 0 || path.StartsWith('/') || path.Split('/').Any(s => s is "" or "." or ".."))
        {
            throw new ArgumentException($"'{path}' is outside the server folder.", nameof(path));
        }

        return $"{Root}/{path}";
    }

    /// <summary>Renames over an existing file: OpenSSH's posix-rename when offered, else delete and rename.</summary>
    private async Task ReplaceAsync(string from, string to, CancellationToken cancellationToken)
    {
        try
        {
            sftp.RenameFile(from, to, isPosix: true);
        }
        catch (Exception e) when (e is SshException or NotSupportedException)
        {
            if (await sftp.ExistsAsync(to, cancellationToken).ConfigureAwait(false))
            {
                await sftp.DeleteFileAsync(to, cancellationToken).ConfigureAwait(false);
            }

            await sftp.RenameFileAsync(from, to, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CreateParentsAsync(string full, CancellationToken cancellationToken)
    {
        var parts = full.Split('/');
        var current = full.StartsWith('/') ? string.Empty : null;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Length == 0)
            {
                continue;
            }

            current = current is null ? parts[i] : $"{current}/{parts[i]}";
            await EnsureDirectoryAsync(current, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureDirectoryAsync(string full, CancellationToken cancellationToken)
    {
        if (_knownDirectories.ContainsKey(full))
        {
            return;
        }

        if (!await sftp.ExistsAsync(full, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await sftp.CreateDirectoryAsync(full, cancellationToken).ConfigureAwait(false);
            }
            catch (SshException)
            {
                // Another file in the same new folder may have made it first.
                if (!await sftp.ExistsAsync(full, cancellationToken).ConfigureAwait(false))
                {
                    throw;
                }
            }
        }

        _knownDirectories.TryAdd(full, 0);
    }
}
