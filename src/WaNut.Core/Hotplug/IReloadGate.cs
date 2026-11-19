using WaNut.Core.Hosting;

namespace WaNut.Core.Hotplug;

/// <summary>
/// Asked once the packs passed the pre-flight and before the VPS is touched, e.g. to hold a reload back while the
/// server is busy (<see cref="Safety.LoadReloadGate"/>). It may wait (a queue until the server is quiet); the hot-plug
/// lock isn't held yet, so waiting blocks nobody else. With none, the hot-plug goes ahead.
/// </summary>
public interface IReloadGate
{
    /// <summary>Null to go ahead, or the reason to stop for the user.</summary>
    Task<string?> CheckAsync(IServerConsole console, CancellationToken cancellationToken);
}
