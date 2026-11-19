namespace WaNut.Core.Hotplug.Readiness;

/// <summary>The inbox a <see cref="ReadinessGate"/> waits on: a folder on this PC or on the VPS. Names are file names, no folders.</summary>
public interface IReadinessSource
{
    /// <summary>The files now in the inbox (not folders). A folder that doesn't exist yet counts as empty.</summary>
    Task<IReadOnlyList<ReadinessEntry>> SnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether nothing else holds the file open for writing: on this PC, an exclusive open succeeds.</summary>
    Task<bool> CanReadExclusivelyAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>The text of a small file, such as a manifest.</summary>
    Task<string> ReadTextAsync(string name, CancellationToken cancellationToken = default);
}
