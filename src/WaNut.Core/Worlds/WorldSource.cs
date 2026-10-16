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
    /// True when the world was played with Fabric mods WaNut can look for, which only a Fabric server loads. Other
    /// loaders (Forge, Quilt) wait for their own server types.
    /// </summary>
    public bool HasFabricMods => Details.ModLoaders.Contains("fabric", StringComparer.OrdinalIgnoreCase) && ModPaths.Count > 0;

    /// <summary>"Install the mods too" is offered for a world with Fabric mods going to a Fabric server.</summary>
    public bool CanInstallModsOn(ServerType? type) => type == ServerType.Fabric && HasFabricMods;
}
