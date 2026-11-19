using WaNut.Core.Hotplug.Preflight;

namespace WaNut.Core.Hotplug;

/// <summary>How a hot-plug ended.</summary>
/// <param name="Outcome"><see cref="HotplugState.Completed"/>, <see cref="HotplugState.RolledBack"/> or <see cref="HotplugState.Aborted"/>.</param>
/// <param name="Message">Why it was refused, stopped or rolled back, or a warning on a completed run; English, for the log and the UI.</param>
/// <param name="Items">What changed (or was undone again, for a rollback).</param>
/// <param name="Inspections">The pre-flight result of every pack to add.</param>
/// <param name="Holder">Who holds the lock, when that is why the run didn't start.</param>
/// <param name="SwapToReload">From the first rename to /reload being sent.</param>
/// <param name="ConsoleLines">The console's error lines that made the run roll back, for the user to read.</param>
public sealed record HotplugReport(
    HotplugState Outcome,
    string? Message,
    IReadOnlyList<HotplugItem> Items,
    IReadOnlyList<DatapackInspection> Inspections,
    HotplugLockHolder? Holder = null,
    TimeSpan? SwapToReload = null,
    IReadOnlyList<string>? ConsoleLines = null)
{
    public bool Succeeded => Outcome == HotplugState.Completed;
}
