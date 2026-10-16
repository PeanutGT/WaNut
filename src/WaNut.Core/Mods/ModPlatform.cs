using WaNut.Core.Instances;

namespace WaNut.Core.Mods;

/// <summary>
/// What a server type can load from Modrinth: which loaders to search for and which folder the files go in.
/// </summary>
public sealed record ModPlatform(ModKind Kind, IReadOnlyList<string> Loaders, string FolderName)
{
    /// <summary>
    /// Null for Vanilla, which loads neither. Paper also runs Spigot and Bukkit plugins, so those are searched too.
    /// </summary>
    public static ModPlatform? For(ServerType type) => type switch
    {
        ServerType.Fabric => new ModPlatform(ModKind.Mod, ["fabric"], "mods"),
        ServerType.Paper => new ModPlatform(ModKind.Plugin, ["paper", "spigot", "bukkit"], "plugins"),
        _ => null,
    };

    public string FolderIn(string instanceDir) => Path.Combine(instanceDir, FolderName);
}
