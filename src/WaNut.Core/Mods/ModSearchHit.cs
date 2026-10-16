namespace WaNut.Core.Mods;

/// <summary>One project in Modrinth's search results.</summary>
public sealed record ModSearchHit(
    string ProjectId,
    string Slug,
    string Title,
    string Description,
    string Author,
    long Downloads,
    string ProjectType)
{
    /// <summary>The project's page on modrinth.com, e.g. https://modrinth.com/mod/sodium.</summary>
    public Uri PageUrl => new($"https://modrinth.com/{Uri.EscapeDataString(ProjectType)}/{Uri.EscapeDataString(Slug)}");
}
