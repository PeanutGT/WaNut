namespace WaNut.Core.Worlds;

/// <summary>What level.dat says about a world.</summary>
/// <param name="LevelName">The name the world has in Minecraft's world list.</param>
/// <param name="VersionName">The Minecraft version that last saved it, e.g. "1.21.1"; null before 1.9, which did not record it.</param>
/// <param name="ModLoaders">Loaders other than Vanilla that have opened the world (from ServerBrands), e.g. "fabric".</param>
/// <param name="HasPlayer">True when level.dat holds the single-player host's inventory and position.</param>
public sealed record WorldDetails(
    string LevelName,
    string? VersionName,
    DateTimeOffset? LastPlayed,
    IReadOnlyList<string> ModLoaders,
    bool HasPlayer)
{
    public bool IsModded => ModLoaders.Count > 0;
}
