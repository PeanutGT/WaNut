namespace WaNut.Core.Mods;

/// <summary>What a mod jar says about itself: its mod id, display name and version, when it has them.</summary>
public sealed record ModJar(string FileName, string Id, string? Name, string? Version);
