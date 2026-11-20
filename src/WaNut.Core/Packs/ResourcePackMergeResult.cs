namespace WaNut.Core.Packs;

/// <summary>The outcome of merging resource packs into one zip.</summary>
/// <param name="Sha1">SHA-1 of the merged zip, for server.properties.</param>
/// <param name="FileCount">Files in the merged zip.</param>
/// <param name="Overlaps">Files found in more than one pack, with the pack that won and the ones it replaced.</param>
public sealed record ResourcePackMergeResult(string Sha1, int FileCount, IReadOnlyList<ResourcePackOverlap> Overlaps);

/// <summary>One file that several packs contain. The first pack in the order wins.</summary>
public sealed record ResourcePackOverlap(string Path, string Winner, IReadOnlyList<string> Losers);
