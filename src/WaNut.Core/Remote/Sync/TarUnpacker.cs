using System.Formats.Tar;

namespace WaNut.Core.Remote.Sync;

/// <summary>Reads a tar stream and writes exactly the files that were asked for into a tree.</summary>
public static class TarUnpacker
{
    /// <exception cref="BulkTransferException">The archive held something unexpected, a different size, or missed a file.</exception>
    public static async Task UnpackAsync(
        Stream archive,
        IReadOnlyList<SyncEntry> expected,
        ISyncTree target,
        Action<long> reportDone,
        CancellationToken cancellationToken)
    {
        var wanted = expected.ToDictionary(e => e.Path, StringComparer.Ordinal);
        long done = 0;
        using var reader = new TarReader(archive, leaveOpen: true);
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
            {
                continue;
            }

            // Only names WaNut asked for are written, so nothing in the archive can land outside the plan.
            var name = entry.Name.StartsWith("./", StringComparison.Ordinal) ? entry.Name[2..] : entry.Name;
            if (!wanted.Remove(name, out var file))
            {
                throw new BulkTransferException($"The archive held '{entry.Name}', which wasn't asked for.");
            }

            if (entry.Length != file.Size)
            {
                throw new BulkTransferException($"{file.Path} changed size while it was being copied.");
            }

            var data = entry.DataStream ?? Stream.Null;
            var before = done;
            await target.WriteAsync(file.Path, new CountingStream(data, n => reportDone(before + n)), file.ModifiedUtc, cancellationToken).ConfigureAwait(false);
            done += file.Size;
            reportDone(done);
        }

        if (wanted.Count > 0)
        {
            throw new BulkTransferException($"The archive was missing {wanted.Count} file(s), such as {wanted.Keys.First()}.");
        }
    }
}
