namespace WaNut.Core.Mods;

/// <summary>What happened to each mod of a single-player instance when the server got its own copies.</summary>
/// <param name="Installed">Titles of everything installed, dependencies included.</param>
/// <param name="NoServerVersion">Titles of mods (or required dependencies) with no version for this server.</param>
/// <param name="ClientOnly">Titles of mods that only run in the game, which were skipped.</param>
/// <param name="NotOnModrinth">File names of jars Modrinth does not know, which were not copied.</param>
public sealed record ModCopyResult(
    IReadOnlyList<string> Installed,
    IReadOnlyList<string> NoServerVersion,
    IReadOnlyList<string> ClientOnly,
    IReadOnlyList<string> NotOnModrinth);
