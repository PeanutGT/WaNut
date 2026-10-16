using WaNut.Core.Instances;

namespace WaNut.Core.Versions;

/// <summary>
/// The Minecraft versions offered for each server type: Mojang's releases from 1.16.5 on, narrowed to those
/// Paper, Fabric, Forge or NeoForge support. Mojang's data stays the source for each version's details and Java version.
/// </summary>
public sealed class ServerVersionCatalog(MojangVersionService mojang, PaperClient paper, FabricClient fabric, ForgeClient forge, NeoForgeClient neoForge)
{
    public async Task<IReadOnlyList<GameVersion>> GetVersionsAsync(ServerType type, CancellationToken cancellationToken = default)
    {
        var releases = await mojang.GetReleasesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlySet<string>? supported = type switch
        {
            ServerType.Paper => await paper.GetVersionIdsAsync(cancellationToken).ConfigureAwait(false),
            ServerType.Fabric => await fabric.GetStableGameVersionsAsync(cancellationToken).ConfigureAwait(false),
            ServerType.Forge => await forge.GetGameVersionsAsync(cancellationToken).ConfigureAwait(false),
            ServerType.NeoForge => await neoForge.GetGameVersionsAsync(cancellationToken).ConfigureAwait(false),
            _ => null,
        };

        return supported is null ? releases : releases.Where(r => supported.Contains(r.Id)).ToList();
    }
}
