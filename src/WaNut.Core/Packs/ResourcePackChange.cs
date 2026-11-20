namespace WaNut.Core.Packs;

/// <summary>What happened when the resource pack list changed.</summary>
/// <param name="Changed">False when nothing was done, such as adding a pack the list already has.</param>
/// <param name="Overlaps">Files that several packs contain, when two or more packs are now merged.</param>
public sealed record ResourcePackChange(bool Changed, IReadOnlyList<ResourcePackOverlap> Overlaps);
