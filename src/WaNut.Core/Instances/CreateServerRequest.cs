using WaNut.Core.Settings;
using WaNut.Core.Versions;

namespace WaNut.Core.Instances;

/// <param name="OwnerName">
/// The host's own Minecraft name, or null to skip. It goes on the whitelist and the OP list at the first start,
/// because the whitelist is on for every new server.
/// </param>
/// <param name="World">The seed and terrain of the new world; null for a random Normal world.</param>
/// <param name="LoaderVersion">
/// Fabric only: the Fabric Loader version to set up, as a modpack asks for; null for the latest stable one.
/// </param>
public sealed record CreateServerRequest(
    string Name,
    GameVersion Version,
    int MemoryMb,
    bool EulaAccepted,
    ServerType Type = ServerType.Vanilla,
    string? OwnerName = null,
    WorldGeneration? World = null,
    string? LoaderVersion = null);
