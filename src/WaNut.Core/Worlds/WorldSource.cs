using WaNut.Core.Instances;

namespace WaNut.Core.Worlds;

/// <summary>A world that can be imported: a folder with level.dat, or a zip that holds one.</summary>
/// <param name="Path">The world folder, or the zip file.</param>
/// <param name="ZipRoot">For a zip, the folder inside it that holds level.dat ("" at the top, or e.g. "My World/"); null for a folder.</param>
/// <param name="Location">The launcher instance it was found in; null when the user picked it.</param>
/// <param name="ModFiles">The mods of that instance, for a modded world.</param>
public sealed record WorldSource(string Path, string? ZipRoot, WorldDetails Details, SaveLocation? Location, IReadOnlyList<string> ModFiles)
{
    public bool IsZip => ZipRoot is not null;

    /// <summary>The full paths of <see cref="ModFiles"/>; empty when the world did not come from a launcher instance.</summary>
    public IReadOnlyList<string> ModPaths => Location is null
        ? []
        : ModFiles.Select(f => System.IO.Path.Combine(Location.GameDir, "mods", f)).ToList();

    /// <summary>
    /// The server type that loads the world's mods, when they are mods WaNut can look for: Fabric, Forge or NeoForge
    /// (from the brands that opened the world). Null for other loaders (Quilt), or when the world did not come from a
    /// launcher instance with mods.
    /// </summary>
    public ServerType? ModServerType => ModPaths.Count == 0 ? null : LoaderTypes.FirstOrDefault(l => Details.ModLoaders.Contains(l.Brand, StringComparer.OrdinalIgnoreCase)).Type;

    /// <summary>"Install the mods too" is offered for a world going to a server of its mods' type.</summary>
    public bool CanInstallModsOn(ServerType? type) => type is not null && type == ModServerType;

    // NeoForge first: a world opened by both was most likely moved on from Forge.
    private static readonly (string Brand, ServerType? Type)[] LoaderTypes =
    [
        ("neoforge", ServerType.NeoForge),
        ("forge", ServerType.Forge),
        ("fabric", ServerType.Fabric),
    ];
}
