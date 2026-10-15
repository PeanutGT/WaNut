using WaNut.Core.Versions;

namespace WaNut.Core.Instances;

/// <param name="OwnerName">
/// The host's own Minecraft name, or null to skip. It goes on the whitelist and the OP list at the first start,
/// because the whitelist is on for every new server.
/// </param>
public sealed record CreateServerRequest(
    string Name,
    GameVersion Version,
    int MemoryMb,
    bool EulaAccepted,
    ServerType Type = ServerType.Vanilla,
    string? OwnerName = null);
