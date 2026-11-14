namespace WaNut.Core.Mods;

/// <summary>What happened to the jars dropped on the Mods tab.</summary>
/// <param name="Added">Names of the jars copied in (the Modrinth title when known).</param>
/// <param name="Skipped">Jars left out because the server already has that file or project.</param>
/// <param name="NotJars">Dropped files that are not .jar files.</param>
/// <param name="ClientOnly">Added jars Modrinth says only run in the game; they can stop the server from starting.</param>
public sealed record JarAddResult(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> NotJars,
    IReadOnlyList<string> ClientOnly);
