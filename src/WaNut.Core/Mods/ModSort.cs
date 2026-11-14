namespace WaNut.Core.Mods;

/// <summary>The orders Modrinth's search offers.</summary>
public enum ModSort
{
    /// <summary>Most downloaded first.</summary>
    Downloads,

    /// <summary>Best match for the search words.</summary>
    Relevance,

    /// <summary>Newest projects first.</summary>
    Newest,

    /// <summary>Most recently updated first.</summary>
    Updated,
}
