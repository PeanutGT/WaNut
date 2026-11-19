namespace WaNut.Core.Hotplug;

/// <summary>Where later safety checks plug into a hot-plug. Both are optional.</summary>
/// <param name="Gate">Asked before the world changes (load and player checks).</param>
/// <param name="Sentinel">Watches the console after /reload; a failure rolls back.</param>
public sealed record HotplugHooks(IReloadGate? Gate = null, IReloadSentinel? Sentinel = null)
{
    public static readonly HotplugHooks None = new();
}
