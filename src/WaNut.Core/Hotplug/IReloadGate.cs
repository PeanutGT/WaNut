using WaNut.Core.Hosting;

namespace WaNut.Core.Hotplug;

/// <summary>
/// Asked before anything in the world changes, e.g. to hold a reload back while the server is busy (MSPT, players).
/// Phase 3 plugs in here; with none, the hot-plug goes ahead.
/// </summary>
public interface IReloadGate
{
    /// <summary>Null to go ahead, or the reason to stop for the user.</summary>
    Task<string?> CheckAsync(IServerConsole console, CancellationToken cancellationToken);
}
