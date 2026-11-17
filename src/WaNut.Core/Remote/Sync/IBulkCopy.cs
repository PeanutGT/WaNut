namespace WaNut.Core.Remote.Sync;

/// <summary>
/// Copies many files at once through one stream instead of one request per file. <see cref="FolderSync"/> uses it when
/// the VPS can pack and unpack archives, and falls back to copying file by file when this throws
/// <see cref="BulkTransferException"/>.
/// </summary>
public interface IBulkCopy
{
    /// <param name="files">What <see cref="FolderSync.Plan"/> wants copied.</param>
    /// <param name="reportDone">Bytes of file content copied so far, never going down.</param>
    /// <exception cref="BulkTransferException">The VPS couldn't do the transfer; nothing is left half done under a real name.</exception>
    Task CopyAsync(IReadOnlyList<SyncEntry> files, Action<long> reportDone, CancellationToken cancellationToken = default);
}
