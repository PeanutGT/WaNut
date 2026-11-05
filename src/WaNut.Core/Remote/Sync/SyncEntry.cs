namespace WaNut.Core.Remote.Sync;

/// <summary>One file or folder in a synced tree. <see cref="Path"/> is relative to the tree's root and uses '/'.</summary>
public sealed record SyncEntry(string Path, bool IsDirectory, long Size, DateTime ModifiedUtc);
