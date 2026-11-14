namespace WaNut.Core.Packs;

/// <summary>The resource pack server.properties offers players on join.</summary>
/// <param name="Url">The download players' games use.</param>
/// <param name="Sha1">Lets games check the download and reuse a copy they already have.</param>
/// <param name="Required">Players who say no are disconnected (Minecraft 1.17+).</param>
/// <param name="Title">The Modrinth name when WaNut set it; null for a pack set by hand.</param>
public sealed record ServerResourcePack(string Url, string? Sha1, bool Required, string? Title = null);
