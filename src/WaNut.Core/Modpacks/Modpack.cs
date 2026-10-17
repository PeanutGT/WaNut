using WaNut.Core.Instances;

namespace WaNut.Core.Modpacks;

/// <summary>What a Modrinth .mrpack or a CurseForge server pack holds, as far as a server needs it.</summary>
/// <param name="Version">The pack's own version, e.g. "1.4.2".</param>
/// <param name="LoaderVersion">The Fabric, Forge or NeoForge version the pack was made with; null for a Vanilla pack.</param>
/// <param name="Files">Files to download, without the ones marked as not used on a server.</param>
/// <param name="SkippedFiles">How many files only the game uses (shaders, minimaps), which the server skips.</param>
/// <param name="ServerPackRoot">
/// For a CurseForge server pack: the folder inside the zip that holds the server files ("" for the top, or e.g.
/// "Pack-1.0/"). Null for a Modrinth pack.
/// </param>
/// <param name="ModCount">For a CurseForge server pack: how many jars its mods folder holds.</param>
public sealed record Modpack(
    string Name,
    string Version,
    string MinecraftVersion,
    ServerType Type,
    string? LoaderVersion,
    IReadOnlyList<ModpackFile> Files,
    int SkippedFiles,
    string? ServerPackRoot = null,
    int ModCount = 0)
{
    public bool IsServerPack => ServerPackRoot is not null;
}
