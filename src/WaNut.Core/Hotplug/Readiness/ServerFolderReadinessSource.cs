using WaNut.Core.Storage;

namespace WaNut.Core.Hotplug.Readiness;

/// <summary>
/// An inbox folder inside a server folder, on this PC or the VPS. SFTP can't ask whether another process still writes
/// a file, so on the VPS stable size and time plus the debounce window stand in for the exclusive-read check.
/// </summary>
public sealed class ServerFolderReadinessSource(IServerFileSystem files, string folder) : IReadinessSource
{
    public async Task<IReadOnlyList<ReadinessEntry>> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var listed = await files.ListDirectoryAsync(folder, cancellationToken: cancellationToken).ConfigureAwait(false);
            return listed.Where(n => !n.IsDirectory).Select(n => new ReadinessEntry(n.Name, n.SizeBytes, n.LastModifiedUtc)).ToList();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    public Task<bool> CanReadExclusivelyAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<string> ReadTextAsync(string name, CancellationToken cancellationToken = default) =>
        files.ReadAllTextAsync(ServerPath.Combine(ServerPath.Normalize(folder), name), cancellationToken);
}
