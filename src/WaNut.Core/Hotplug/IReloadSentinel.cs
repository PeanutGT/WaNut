using WaNut.Core.Hosting;

namespace WaNut.Core.Hotplug;

/// <summary>
/// Watches the console around /reload for signs the new data failed to load. Begun before /reload is sent so no line
/// is missed; a failure rolls the swap back. Phase 3's log sentinel plugs in here.
/// </summary>
public interface IReloadSentinel
{
    IReloadWatch Begin(IServerConsole console);
}

/// <summary>One watch over one /reload. Dispose stops listening.</summary>
public interface IReloadWatch : IDisposable
{
    /// <summary>Null when the reload looks fine, or a summary of the errors seen.</summary>
    Task<string?> FailureAsync(CancellationToken cancellationToken);
}
