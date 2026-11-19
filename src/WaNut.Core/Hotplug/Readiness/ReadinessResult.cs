namespace WaNut.Core.Hotplug.Readiness;

/// <param name="Files">The complete batch, by name; empty unless <see cref="ReadinessOutcome.Ready"/>.</param>
/// <param name="Message">Why it didn't become ready, for the user.</param>
public sealed record ReadinessResult(ReadinessOutcome Outcome, IReadOnlyList<ReadinessEntry> Files, string? Message = null)
{
    public bool IsReady => Outcome == ReadinessOutcome.Ready;
}
