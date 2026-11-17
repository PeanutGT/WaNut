using System.IO.Compression;
using System.Text;
using Renci.SshNet;

namespace WaNut.Core.Remote.Sync;

/// <summary>
/// Brings files back from the VPS as one compressed tar stream. Each file is still written under a temporary name and
/// renamed, with its size checked against the listing, so a cut connection leaves only whole files behind.
/// </summary>
public sealed class SshDownloadCopy(SshClient ssh, string remoteRoot, ISyncTree target) : IBulkCopy
{
    public async Task CopyAsync(IReadOnlyList<SyncEntry> files, Action<long> reportDone, CancellationToken cancellationToken = default)
    {
        using var pack = ssh.CreateCommand(TarCommands.PackCommand(remoteRoot), Encoding.UTF8);
        var running = pack.ExecuteAsync(cancellationToken);

        // The names go in while the archive comes out: a long list must not wait for output nobody is reading yet.
        var names = Task.Run(async () =>
        {
            await using var stdin = pack.CreateInputStream();
            foreach (var file in files)
            {
                await stdin.WriteAsync(Encoding.UTF8.GetBytes(file.Path + '\0'), cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);

        try
        {
            await using var gzip = new GZipStream(pack.OutputStream, CompressionMode.Decompress, leaveOpen: true);
            await TarUnpacker.UnpackAsync(gzip, files, target, reportDone, cancellationToken).ConfigureAwait(false);

            // The archive ends with padding the reader doesn't need; reading it lets the remote command finish.
            await gzip.CopyToAsync(Stream.Null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            throw new BulkTransferException($"The archive from the VPS was damaged: {e.Message}", e);
        }

        await names.ConfigureAwait(false);
        await running.ConfigureAwait(false);
        if (pack.ExitStatus is not 0)
        {
            throw new BulkTransferException($"The VPS couldn't pack the files ({pack.ExitStatus}): {pack.Error.Trim()}");
        }
    }
}
