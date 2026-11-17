using System.Formats.Tar;

namespace WaNut.Core.Remote.Sync;

/// <summary>Writes files from a tree into one tar stream (GNU format: long names, whole-second times).</summary>
public static class TarPacker
{
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    /// <exception cref="IOException">A file's size changed since it was listed.</exception>
    public static async Task PackAsync(IReadOnlyList<SyncEntry> files, ISyncTree source, Stream destination, CancellationToken cancellationToken)
    {
        await using var writer = new TarWriter(destination, TarEntryFormat.Gnu, leaveOpen: true);
        foreach (var file in files)
        {
            await using var read = await source.OpenReadAsync(file.Path, cancellationToken).ConfigureAwait(false);

            // The tar header carries the size up front, so a file that changed since the listing can't be packed.
            if (read.CanSeek && read.Length != file.Size)
            {
                throw new IOException($"{file.Path} changed while it was being copied.");
            }

            var entry = new GnuTarEntry(TarEntryType.RegularFile, file.Path)
            {
                ModificationTime = new DateTimeOffset(DateTime.SpecifyKind(file.ModifiedUtc, DateTimeKind.Utc)),
                Mode = FileMode,
                DataStream = read,
            };
            await writer.WriteEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }
}
