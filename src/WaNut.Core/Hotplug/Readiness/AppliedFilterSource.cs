namespace WaNut.Core.Hotplug.Readiness;

/// <summary>
/// A watched folder as the gate should see it: only .zip files, and none that were already applied and haven't
/// changed since, so a folder of finished packs waits quietly until one of them is saved again.
/// </summary>
public sealed class AppliedFilterSource(IReadinessSource inner) : IReadinessSource
{
    private readonly Dictionary<string, (long Size, DateTime Time)> _applied = new(StringComparer.Ordinal);

    /// <summary>Remembers these files as applied in their current size and time.</summary>
    public void MarkApplied(IEnumerable<ReadinessEntry> files)
    {
        foreach (var file in files)
        {
            _applied[file.Name] = (file.SizeBytes, file.ModifiedUtc);
        }
    }

    public async Task<IReadOnlyList<ReadinessEntry>> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        var all = await inner.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        return all.Where(e => e.Name.EndsWith(HotplugNames.Extension, StringComparison.OrdinalIgnoreCase)
                && !(_applied.TryGetValue(e.Name, out var seen) && seen == (e.SizeBytes, e.ModifiedUtc)))
            .ToList();
    }

    public Task<bool> CanReadExclusivelyAsync(string name, CancellationToken cancellationToken = default) => inner.CanReadExclusivelyAsync(name, cancellationToken);

    public Task<string> ReadTextAsync(string name, CancellationToken cancellationToken = default) => inner.ReadTextAsync(name, cancellationToken);
}
