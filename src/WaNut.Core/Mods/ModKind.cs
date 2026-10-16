namespace WaNut.Core.Mods;

/// <summary>Fabric, Forge and NeoForge load mods; Paper loads plugins. They live in different folders and come from different Modrinth loaders.</summary>
public enum ModKind
{
    Mod,
    Plugin,
}
