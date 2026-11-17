using System.IO.Compression;
using System.Text;
using Renci.SshNet;

namespace WaNut.Core.Remote.Sync;

/// <summary>
/// Sends files to the VPS as one compressed tar stream over a single SSH channel, so a thousand small files cost one
/// request instead of thousands. Files go in batches: each batch is unpacked into a stage folder on the VPS, checked
/// against the planned sizes, and only then moved over the real files, so a cut connection never leaves a half-written
/// file under its real name and a repeated sync only sends what is still missing.
/// </summary>
public sealed class SshUploadCopy(SshClient ssh, ISyncTree source, string remoteRoot) : IBulkCopy
{
    /// <summary>Content bytes per batch.</summary>
    public const long BatchBytes = 256L * 1024 * 1024;

    public static IEnumerable<IReadOnlyList<SyncEntry>> Batches(IReadOnlyList<SyncEntry> files)
    {
        var batch = new List<SyncEntry>();
        long bytes = 0;
        foreach (var file in files)
        {
            if (batch.Count > 0 && bytes + file.Size > BatchBytes)
            {
                yield return batch;
                batch = [];
                bytes = 0;
            }

            batch.Add(file);
            bytes += file.Size;
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    public async Task CopyAsync(IReadOnlyList<SyncEntry> files, Action<long> reportDone, CancellationToken cancellationToken = default)
    {
        long before = 0;
        foreach (var batch in Batches(files))
        {
            var start = before;
            await SendAsync(batch, n => reportDone(start + Math.Min(n, batch.Sum(f => f.Size))), cancellationToken).ConfigureAwait(false);
            before += batch.Sum(f => f.Size);
            reportDone(before);
        }
    }

    private async Task SendAsync(IReadOnlyList<SyncEntry> batch, Action<long> reportDone, CancellationToken cancellationToken)
    {
        using (var extract = ssh.CreateCommand(TarCommands.ExtractCommand(remoteRoot), Encoding.UTF8))
        {
            var running = extract.ExecuteAsync(cancellationToken);
            try
            {
                await using var stdin = extract.CreateInputStream();
                await using (var gzip = new GZipStream(stdin, CompressionLevel.Fastest, leaveOpen: true))
                {
                    await TarPacker.PackAsync(batch, source, new ProgressWriteStream(gzip, reportDone), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException && e is not BulkTransferException && extract.ExitStatus is not null and not 0)
            {
                throw Failed(extract);
            }

            await running.ConfigureAwait(false);
            if (extract.ExitStatus is not 0)
            {
                throw Failed(extract);
            }
        }

        await CheckStageAsync(batch, cancellationToken).ConfigureAwait(false);
        await RunAsync(TarCommands.MoveCommand(remoteRoot), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Every planned file is in the stage folder with the planned size; if not, nothing is moved and the stage is cleared.</summary>
    private async Task CheckStageAsync(IReadOnlyList<SyncEntry> batch, CancellationToken cancellationToken)
    {
        var staged = (await RunAsync(TarCommands.StageListCommand(remoteRoot), cancellationToken).ConfigureAwait(false))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(r => r.Split('\t', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[1], p => long.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal);

        var bad = batch.FirstOrDefault(f => !staged.TryGetValue(f.Path, out var size) || size != f.Size);
        if (bad is not null || staged.Count != batch.Count)
        {
            await RunAsync(TarCommands.ClearStageCommand(remoteRoot), CancellationToken.None).ConfigureAwait(false);
            throw new BulkTransferException(bad is null
                ? "The VPS unpacked a different number of files than were sent."
                : $"The VPS kept a different size of {bad.Path} than was sent. Is its disk full?");
        }
    }

    private async Task<string> RunAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            return await VpsConnector.RunAsync(ssh, command, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException e)
        {
            throw new BulkTransferException(e.Message, e);
        }
    }

    private static BulkTransferException Failed(SshCommand command) =>
        new($"The VPS couldn't unpack the files ({command.ExitStatus}): {command.Error.Trim()}");
}
