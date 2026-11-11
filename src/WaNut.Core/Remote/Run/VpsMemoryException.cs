using WaNut.Core.Hosting;

namespace WaNut.Core.Remote.Run;

/// <summary>The server asks for more RAM than the VPS can give it.</summary>
public sealed class VpsMemoryException(long vpsMemoryMb)
    : Exception($"The VPS has {vpsMemoryMb} MB; a server there may use at most {MemoryLimits.MaxServerMb(vpsMemoryMb)} MB.")
{
    public long VpsMemoryMb { get; } = vpsMemoryMb;

    public int MaxServerMb => MemoryLimits.MaxServerMb(VpsMemoryMb);
}
