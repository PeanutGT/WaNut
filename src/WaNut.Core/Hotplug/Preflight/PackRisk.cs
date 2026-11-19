namespace WaNut.Core.Hotplug.Preflight;

/// <summary>How safely a data pack's contents can be swapped in while the server runs.</summary>
public enum PackRisk
{
    /// <summary>Functions, loot tables, recipes, advancements, predicates, tags and the like: /reload applies them.</summary>
    Safe,

    /// <summary>
    /// World generation and the other registries Minecraft reads only when the world opens (enchantments, damage
    /// types and so on): hot-plugging works, but these parts only apply after a restart, and terrain only in new chunks.
    /// </summary>
    AppliesAfterRestart,

    /// <summary>New dimensions or dimension types: swapping them in live is refused, the server must be restarted.</summary>
    NeedsRestart,
}
