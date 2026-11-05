using Renci.SshNet;
using Renci.SshNet.Common;

namespace WaNut.Core.Remote.Sync;

/// <summary>A server folder on the VPS, reached over SFTP. <paramref name="root"/> is relative to the user's home.</summary>
public sealed class SftpSyncTree(SftpClient sftp, string root) : ISyncTree
{
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
        var entries = new List<SyncEntry>();
        if (await sftp.ExistsAsync(Root, cancellationToken).ConfigureAwait(false))
        {
            await WalkAsync(Root, string.Empty, entries, cancellationToken).ConfigureAwait(false);
        }

        return entries;
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
            if (sftp.GetAttributes(temp).Size != sent)
            {
                throw new IOException($"The VPS kept fewer bytes of {path} than were sent. Is its disk full?");
            }

            sftp.SetLastWriteTimeUtc(temp, modifiedUtc);
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
        if (!await sftp.ExistsAsync(full, cancellationToken).ConfigureAwait(false))
        {
            await sftp.CreateDirectoryAsync(full, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default) =>
        sftp.DeleteFileAsync(FullPath(path), cancellationToken);

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        sftp.DeleteDirectoryAsync(FullPath(path), cancellationToken);

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
            if (!await sftp.ExistsAsync(current, cancellationToken).ConfigureAwait(false))
            {
                await sftp.CreateDirectoryAsync(current, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task WalkAsync(string directory, string prefix, List<SyncEntry> entries, CancellationToken cancellationToken)
    {
        var subdirectories = new List<(string Full, string Relative)>();
        await foreach (var file in sftp.ListDirectoryAsync(directory, cancellationToken).ConfigureAwait(false))
        {
            // Names Windows can't hold as one path part are left on the VPS.
            if (file.Name is "." or ".." || file.Name.Contains('\\'))
            {
                continue;
            }

            var path = prefix + file.Name;
            if (file.IsDirectory)
            {
                entries.Add(new SyncEntry(path, true, 0, file.LastWriteTimeUtc));
                subdirectories.Add(($"{directory}/{file.Name}", path));
            }
            else if (file.IsRegularFile)
            {
                entries.Add(new SyncEntry(path, false, file.Length, file.LastWriteTimeUtc));
            }
        }

        foreach (var (full, relative) in subdirectories)
        {
            await WalkAsync(full, relative + "/", entries, cancellationToken).ConfigureAwait(false);
        }
    }
}
