namespace WaNut.Core.Mods;

/// <summary>A Modrinth project as its project page describes it.</summary>
/// <param name="IsClientOnly">The project says it does not run on a server (minimaps, shaders, HUD mods).</param>
public sealed record ModProject(string Id, string Title, bool IsClientOnly);
