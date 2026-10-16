using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaNut.Core.Mods;

/// <summary>
/// Searches Modrinth's API v2 for mods and plugins and finds the version that fits a server.
/// </summary>
public sealed class ModrinthClient(HttpClient http)
{
    public const int PageSize = 20;

    private const string ApiBase = "https://api.modrinth.com/v2";

    /// <summary>
    /// Search URL limited to the server's loaders and Minecraft version. Fabric results also leave out client-only
    /// mods (minimaps, shaders), which do nothing on a server. An empty query lists the most downloaded projects.
    /// </summary>
    public static Uri SearchUrl(ModPlatform platform, string gameVersion, string query, int offset)
    {
        List<string[]> facets =
        [
            platform.Loaders.Select(l => $"categories:{l}").ToArray(),
            [$"versions:{gameVersion}"],

            // Plugins have been listed both as "mod" and as their own "plugin" type over time.
            ["project_type:mod", "project_type:plugin"],
        ];
        if (platform.Kind == ModKind.Mod)
        {
            facets.Add(["server_side:required", "server_side:optional"]);
        }

        var trimmed = query.Trim();
        var index = trimmed.Length == 0 ? "downloads" : "relevance";
        return new Uri(string.Create(
            CultureInfo.InvariantCulture,
            $"{ApiBase}/search?query={Uri.EscapeDataString(trimmed)}&facets={Escape(facets)}&index={index}&offset={offset}&limit={PageSize}"));
    }

    public static Uri VersionsUrl(string projectId, ModPlatform platform, string gameVersion) =>
        new($"{ApiBase}/project/{Uri.EscapeDataString(projectId)}/version?loaders={Escape(platform.Loaders)}&game_versions={Escape(new[] { gameVersion })}");

    public static readonly Uri VersionFilesUrl = new($"{ApiBase}/version_files");

    public static Uri ProjectUrl(string projectId) => new($"{ApiBase}/project/{Uri.EscapeDataString(projectId)}");

    public async Task<ModSearchPage> SearchAsync(
        ModPlatform platform, string gameVersion, string query, int offset, CancellationToken cancellationToken = default)
    {
        await using var stream = await http.GetStreamAsync(SearchUrl(platform, gameVersion, query, offset), cancellationToken).ConfigureAwait(false);
        return ParseSearch(stream);
    }

    /// <summary>The version to install, or null when the project has none for this server.</summary>
    public async Task<ModVersion?> GetBestVersionAsync(
        string projectId, ModPlatform platform, string gameVersion, CancellationToken cancellationToken = default)
    {
        await using var stream = await http.GetStreamAsync(VersionsUrl(projectId, platform, gameVersion), cancellationToken).ConfigureAwait(false);
        return ParseBestVersion(stream, platform, gameVersion);
    }

    /// <summary>A project's display name, used for dependencies, which Modrinth names only by id.</summary>
    public async Task<string> GetProjectTitleAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var stream = await http.GetStreamAsync(ProjectUrl(projectId), cancellationToken).ConfigureAwait(false);
        var project = await JsonSerializer.DeserializeAsync<ProjectDto>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(project?.Title) ? projectId : project.Title;
    }

    /// <summary>
    /// The Modrinth projects that published these files (SHA-512 hashes). Files Modrinth does not know are left out.
    /// </summary>
    public async Task<IReadOnlySet<string>> IdentifyProjectsAsync(IReadOnlyCollection<string> sha512Hashes, CancellationToken cancellationToken = default)
    {
        if (sha512Hashes.Count == 0)
        {
            return new HashSet<string>();
        }

        var body = JsonSerializer.Serialize(new { hashes = sha512Hashes, algorithm = "sha512" });
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(VersionFilesUrl, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return ParseIdentifiedProjects(stream);
    }

    public static IReadOnlySet<string> ParseIdentifiedProjects(Stream versionFilesJson)
    {
        var versions = JsonSerializer.Deserialize<Dictionary<string, IdentifiedDto>>(versionFilesJson) ?? [];
        return versions.Values.Select(v => v.ProjectId).ToHashSet(StringComparer.Ordinal);
    }

    public static ModSearchPage ParseSearch(Stream searchJson)
    {
        var result = JsonSerializer.Deserialize<SearchDto>(searchJson)
            ?? throw new InvalidDataException("The Modrinth search result is empty.");

        var hits = result.Hits
            .Select(h => new ModSearchHit(h.ProjectId, h.Slug ?? h.ProjectId, h.Title, h.Description ?? string.Empty, h.Author ?? string.Empty, h.Downloads, h.ProjectType ?? "mod"))
            .ToList();
        return new ModSearchPage(hits, result.TotalHits);
    }

    /// <summary>
    /// The newest release that runs on this loader and Minecraft version; a beta or alpha only when there is no
    /// release. Its primary file must be an HTTPS .jar with a SHA-512 checksum.
    /// </summary>
    public static ModVersion? ParseBestVersion(Stream versionsJson, ModPlatform platform, string gameVersion)
    {
        var versions = JsonSerializer.Deserialize<List<VersionDto>>(versionsJson)
            ?? throw new InvalidDataException("The Modrinth version list is empty.");

        var best = versions
            .Where(v => v.GameVersions.Contains(gameVersion, StringComparer.Ordinal)
                && v.Loaders.Any(l => platform.Loaders.Contains(l, StringComparer.Ordinal))
                && v.Files.Count > 0)
            .OrderBy(v => v.VersionType switch { "release" => 0, "beta" => 1, _ => 2 })
            .ThenByDescending(v => v.DatePublished)
            .FirstOrDefault();
        if (best is null)
        {
            return null;
        }

        var file = best.Files.FirstOrDefault(f => f.Primary) ?? best.Files[0];
        var url = new Uri(file.Url);
        if (url.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException($"The Modrinth download {file.Url} is not HTTPS.");
        }

        if (!IsSafeJarName(file.FileName))
        {
            throw new InvalidDataException($"The Modrinth file name \"{file.FileName}\" is not a plain .jar name.");
        }

        var sha512 = file.Hashes?.GetValueOrDefault("sha512")
            ?? throw new InvalidDataException($"The Modrinth file {file.FileName} has no SHA-512 checksum.");

        var required = (best.Dependencies ?? [])
            .Where(d => d.DependencyType == "required" && !string.IsNullOrEmpty(d.ProjectId))
            .Select(d => d.ProjectId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new ModVersion(best.ProjectId, best.Id, best.VersionNumber, file.FileName, url, sha512, required);
    }

    /// <summary>The name comes from the internet and becomes a path: it must stay inside the folder and be a jar.</summary>
    public static bool IsSafeJarName(string name) =>
        name.Length > 4
        && name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) < 0
        && !name.StartsWith('.')
        && name.Trim() == name;

    private static string Escape<T>(T value) => Uri.EscapeDataString(JsonSerializer.Serialize(value));

    private sealed record SearchDto(
        [property: JsonPropertyName("hits")] List<HitDto> Hits,
        [property: JsonPropertyName("total_hits")] int TotalHits);

    private sealed record HitDto(
        [property: JsonPropertyName("project_id")] string ProjectId,
        [property: JsonPropertyName("slug")] string? Slug,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("author")] string? Author,
        [property: JsonPropertyName("downloads")] long Downloads,
        [property: JsonPropertyName("project_type")] string? ProjectType);

    private sealed record IdentifiedDto(
        [property: JsonPropertyName("project_id")] string ProjectId);

    private sealed record ProjectDto(
        [property: JsonPropertyName("title")] string? Title);

    private sealed record VersionDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("project_id")] string ProjectId,
        [property: JsonPropertyName("version_number")] string VersionNumber,
        [property: JsonPropertyName("version_type")] string VersionType,
        [property: JsonPropertyName("date_published")] DateTimeOffset DatePublished,
        [property: JsonPropertyName("game_versions")] List<string> GameVersions,
        [property: JsonPropertyName("loaders")] List<string> Loaders,
        [property: JsonPropertyName("files")] List<FileDto> Files,
        [property: JsonPropertyName("dependencies")] List<DependencyDto>? Dependencies);

    private sealed record FileDto(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("filename")] string FileName,
        [property: JsonPropertyName("primary")] bool Primary,
        [property: JsonPropertyName("hashes")] Dictionary<string, string>? Hashes);

    private sealed record DependencyDto(
        [property: JsonPropertyName("project_id")] string? ProjectId,
        [property: JsonPropertyName("dependency_type")] string DependencyType);
}
