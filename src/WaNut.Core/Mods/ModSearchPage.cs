namespace WaNut.Core.Mods;

/// <summary>One page of search results and how many there are in total.</summary>
public sealed record ModSearchPage(IReadOnlyList<ModSearchHit> Hits, int TotalHits);
