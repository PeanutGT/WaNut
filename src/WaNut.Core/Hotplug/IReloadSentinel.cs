using WaNut.Core.Hosting;

namespace WaNut.Core.Hotplug;

/// <summary>
/// Watches the console around /reload for signs the new data failed to load. Begun before /reload is sent so no line
/// is missed; a failure rolls the swap back (<see cref="Safety.ReloadLogSentinel"/>).
/// </summary>
public interface IReloadSentinel
{
    IReloadWatch Begin(IServerConsole console);
}

/// <summary>One watch over one /reload. Dispose stops listening.</summary>
public interface IReloadWatch : IDisposable
{
    /// <summary>True as soon as an error line was seen, without waiting for the watch window to end.</summary>
    bool HasFailed { get; }

    /// <summary>Waits out the watch window: null when the reload looks fine, or what went wrong.</summary>
    Task<ReloadFailure?> FailureAsync(CancellationToken cancellationToken);
}
