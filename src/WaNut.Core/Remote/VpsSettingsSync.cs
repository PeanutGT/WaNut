using Renci.SshNet;
using WaNut.Core.Instances;
using WaNut.Core.Remote.Sync;
using WaNut.Core.Settings;

namespace WaNut.Core.Remote;

/// <summary>
/// Reads and writes a server's small settings file on the VPS while the rest of its folder stays there, so game
/// settings can change without bringing the world back. Only <see cref="Files"/> are touched, each written whole under
/// a temporary name and renamed, and only while the server is stopped (a running server rewrites its own settings).
/// </summary>
public sealed class VpsSettingsSync(InstanceStore store, VpsConnector connector)
{
    /// <summary>The files that may change on the VPS from here. Paths are relative to the server folder.</summary>
    public static readonly IReadOnlyList<string> Files = [GameSettingsStore.PropertiesFileName];

    // One transfer at a time: two quick edits must reach the VPS in the order they were made.
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Copies the VPS's settings files over this PC's, which are older while the newest files are on the VPS.</summary>
    /// <exception cref="VpsFolderMissingException">The VPS has no settings file for this server.</exception>
    public async Task PullAsync(string id, VpsProfile vps, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var sftp = await connector.ConnectSftpAsync(vps, cancellationToken).ConfigureAwait(false);
            await CopyDownAsync(sftp, id, store.InstanceDir(id), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Sends this PC's settings files to the VPS.</summary>
    /// <exception cref="VpsFolderMissingException">The VPS has no settings file to replace; nothing is created there.</exception>
    public async Task PushAsync(string id, VpsProfile vps, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var sftp = await connector.ConnectSftpAsync(vps, cancellationToken).ConfigureAwait(false);
            await CopyUpAsync(sftp, id, store.InstanceDir(id), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The copy of <see cref="PullAsync"/> over a connection that is already open.</summary>
    public static async Task CopyDownAsync(SftpClient sftp, string id, string localDir, CancellationToken cancellationToken = default)
    {
        var remote = new SftpSyncTree(sftp, SftpSyncTree.ServerRoot(id));
        var local = new LocalSyncTree(localDir);
        foreach (var path in Files)
        {
            var full = $"{remote.Root}/{path}";
            if (!await sftp.ExistsAsync(full, cancellationToken).ConfigureAwait(false))
            {
                throw new VpsFolderMissingException();
            }

            var modified = (await sftp.GetAttributesAsync(full, cancellationToken).ConfigureAwait(false)).LastWriteTimeUtc;
            await using var read = await remote.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
            await local.WriteAsync(path, read, modified, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The copy of <see cref="PushAsync"/> over a connection that is already open.</summary>
    public static async Task CopyUpAsync(SftpClient sftp, string id, string localDir, CancellationToken cancellationToken = default)
    {
        var remote = new SftpSyncTree(sftp, SftpSyncTree.ServerRoot(id));
        foreach (var path in Files)
        {
            if (!await sftp.ExistsAsync($"{remote.Root}/{path}", cancellationToken).ConfigureAwait(false))
            {
                throw new VpsFolderMissingException();
            }

            var file = Path.Combine(localDir, path);
            await using var read = File.OpenRead(file);
            await remote.WriteAsync(path, read, File.GetLastWriteTimeUtc(file), cancellationToken).ConfigureAwait(false);
        }
    }
}
