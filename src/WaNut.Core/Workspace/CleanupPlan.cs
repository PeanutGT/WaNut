namespace WaNut.Core.Workspace;

/// <summary>
/// The dry run of a clean-up: what would be deleted or packed, nothing changed yet. <see cref="FileOrganizer.ExecutePlanAsync"/>
/// only acts on the items the person kept checked.
/// </summary>
public sealed record CleanupPlan(IReadOnlyList<CleanupItem> Items, DateTime EvaluatedUtc)
{
    public bool IsEmpty => Items.Count == 0;

    public bool HasRisk => Items.Any(i => i.Risky);

    /// <summary>The space deleting <paramref name="selected"/> gives back. A packed folder still takes its archive's size, so it is not counted.</summary>
    public static long FreedBytes(IEnumerable<CleanupItem> selected) =>
        selected.Where(i => i.Action == CleanupAction.Delete).Sum(i => i.SizeBytes);
}

/// <summary>What <see cref="FileOrganizer.ExecutePlanAsync"/> did. <see cref="Skipped"/> lists items that changed or vanished since the dry run.</summary>
public sealed record CleanupResult(int Deleted, int Archived, long FreedBytes, IReadOnlyList<CleanupItem> Skipped);
