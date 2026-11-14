namespace WaNut.Core.Mods;

/// <summary>A newer version of an installed jar, for the server's loader and Minecraft version.</summary>
/// <param name="Mod">The jar in the folder now.</param>
/// <param name="Version">The version to put in its place.</param>
/// <param name="Title">The project's name on Modrinth.</param>
public sealed record ModUpdate(InstalledMod Mod, ModVersion Version, string Title);
