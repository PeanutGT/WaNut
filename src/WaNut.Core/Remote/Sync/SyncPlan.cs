namespace WaNut.Core.Remote.Sync;

/// <summary>What one sync does, in this order: delete files, delete folders (deepest first), create folders, copy files.</summary>
public sealed record SyncPlan(
    IReadOnlyList<SyncEntry> DeleteFiles,
    IReadOnlyList<SyncEntry> DeleteDirectories,
    IReadOnlyList<SyncEntry> CreateDirectories,
    IReadOnlyList<SyncEntry> CopyFiles)
{
    public long CopyBytes => CopyFiles.Sum(f => f.Size);

    public bool IsEmpty => DeleteFiles.Count == 0 && DeleteDirectories.Count == 0 && CreateDirectories.Count == 0 && CopyFiles.Count == 0;
}

public sealed record SyncResult(int Copied, int Deleted, long Bytes);
