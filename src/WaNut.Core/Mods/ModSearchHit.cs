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
    /// <summary>The project's icon (HTTPS only), or null when it has none.</summary>
    public Uri? IconUrl { get; init; }

    /// <summary>When the project last changed on Modrinth.</summary>
    public DateTimeOffset? Updated { get; init; }

    /// <summary>"required", "optional" or "unsupported": whether the project runs on a server.</summary>
    public string? ServerSide { get; init; }

    /// <summary>The project's page on modrinth.com, e.g. https://modrinth.com/mod/sodium.</summary>
    public Uri PageUrl => new($"https://modrinth.com/{Uri.EscapeDataString(ProjectType)}/{Uri.EscapeDataString(Slug)}");
}
