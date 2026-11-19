namespace WaNut.Core.Hotplug;

/// <summary>Either the lock we now hold, or who holds it. Dispose <see cref="Lock"/> when the hot-plug ends.</summary>
public sealed record HotplugLockResult(HotplugLock? Lock, HotplugLockHolder? Holder)
{
    public bool Acquired => Lock is not null;
}
