namespace WaNut.Core.Remote.Sync;

/// <summary>A folder that <see cref="FolderSync"/> can read and write: a server folder on this PC or on the VPS.</summary>
public interface ISyncTree
{
    /// <summary>Every file and folder under the root, symbolic links left out. An empty list when the root doesn't exist yet.</summary>
    Task<IReadOnlyList<SyncEntry>> ListAsync(CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/> under a temporary name and then renames it, so an
    /// interrupted sync never leaves a half-written file under the real name. Creates missing parent folders.
    /// </summary>
    Task WriteAsync(string path, Stream content, DateTime modifiedUtc, CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

    Task DeleteFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Removes an empty folder.</summary>
    Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default);
}
