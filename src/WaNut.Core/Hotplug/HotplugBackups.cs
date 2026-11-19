using System.Globalization;
using WaNut.Core.Storage;

namespace WaNut.Core.Hotplug;

/// <summary>
/// The backup folder's naming and clean-up: a replaced datapack is kept as <c>name.20261119T064152Z.zip</c>, and only the
/// newest few of each datapack stay.
/// </summary>
public static class HotplugBackups
{
    public const int DefaultKeep = 5;

    private const string StampFormat = "yyyyMMdd'T'HHmmss'Z'";
    private const int StampLength = 16;

    /// <summary>"loot.zip" at a moment becomes "loot.20261119T064152Z.zip".</summary>
    public static string NameFor(string packFileName, DateTimeOffset when)
    {
        var dot = packFileName.LastIndexOf('.');
        var stem = dot > 0 ? packFileName[..dot] : packFileName;
        var ext = dot > 0 ? packFileName[dot..] : string.Empty;
        return $"{stem}.{when.UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture)}{ext}";
    }

    /// <summary>Splits a backup name back into the datapack's file name and the time it was saved; false for any other file.</summary>
    public static bool TryParse(string backupName, out string packFileName, out DateTimeOffset when)
    {
        packFileName = string.Empty;
        when = default;
        var dot = backupName.LastIndexOf('.');
        var ext = dot > 0 ? backupName[dot..] : string.Empty;
        var body = dot > 0 ? backupName[..dot] : backupName;
        if (body.Length <= StampLength + 1 || body[^(StampLength + 1)] != '.')
        {
            return false;
        }

        if (!DateTimeOffset.TryParseExact(body[^StampLength..], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out when))
        {
            return false;
        }

        packFileName = body[..^(StampLength + 1)] + ext;
        return true;
    }

    /// <summary>
    /// Deletes all but the newest <paramref name="keep"/> backups of each datapack in <paramref name="backupFolder"/>
    /// (relative to the file system's root). Files that aren't backups are left alone. Returns the paths deleted.
    /// </summary>
    public static async Task<IReadOnlyList<string>> PruneAsync(IServerFileSystem files, string backupFolder, int keep = DefaultKeep, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);
        if (!await files.ExistsAsync(backupFolder, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        var listed = await files.ListDirectoryAsync(backupFolder, cancellationToken: cancellationToken).ConfigureAwait(false);
        var old = listed
            .Where(n => !n.IsDirectory)
            .Select(n => TryParse(n.Name, out var pack, out var when) ? (n.Path, Pack: pack, When: when) : default)
            .Where(b => b.Path is not null)
            .GroupBy(b => b.Pack, StringComparer.Ordinal)
            .SelectMany(g => g.OrderByDescending(b => b.When).ThenByDescending(b => b.Path, StringComparer.Ordinal).Skip(keep))
            .Select(b => b.Path)
            .ToList();
        if (old.Count > 0)
        {
            await files.DeleteBatchAsync(old, cancellationToken).ConfigureAwait(false);
        }

        return old;
    }
}
