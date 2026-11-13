using WaNut.Core.Hosting;

namespace WaNut.Core.Remote.Run;

/// <summary>The server asks for more RAM than the VPS can give it.</summary>
public sealed class VpsMemoryException(long vpsMemoryMb)
    : Exception($"The VPS has {vpsMemoryMb} MB; a server there may use at most {MemoryLimits.MaxVpsServerMb(vpsMemoryMb)} MB.")
{
    public long VpsMemoryMb { get; } = vpsMemoryMb;

    public int MaxServerMb => MemoryLimits.MaxVpsServerMb(VpsMemoryMb);

    /// <summary>Not even <see cref="MemoryLimits.MinimumMb"/> fits: no server can run on this VPS.</summary>
    public bool VpsTooSmall => MaxServerMb < MemoryLimits.MinimumMb;
}
