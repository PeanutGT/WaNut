namespace WaNut.Core.Hotplug.Readiness;

/// <summary>What the gate is doing right now, for a status line such as "debounce 2.1 s".</summary>
/// <param name="DebounceRemaining">Only while <see cref="ReadinessPhase.Debouncing"/>.</param>
/// <param name="Detail">What is being waited for, such as the missing file's name.</param>
public sealed record ReadinessStatus(ReadinessPhase Phase, TimeSpan Elapsed, int FileCount, TimeSpan? DebounceRemaining = null, string? Detail = null);
