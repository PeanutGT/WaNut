using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WaNut.Core.Http;

namespace WaNut.Core.Mods;

/// <summary>
/// Searches Modrinth's API v2 for mods and plugins and finds the version that fits a server. Requests turned away
/// for going over Modrinth's rate limit are sent again after the wait it asks for.
/// </summary>
/// <param name="delay">Waits before sending a rate-limited request again; tests pass one that returns at once.</param>
public sealed class ModrinthClient(HttpClient http, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public const int PageSize = 20;

    private const string ApiBase = "https://api.modrinth.com/v2";

    /// <summary>
    /// Search URL limited to the server's loaders and Minecraft version. Fabric results also leave out client-only
    /// mods (minimaps, shaders), which do nothing on a server. An empty query lists the most downloaded projects.
    /// </summary>
    /// <param name="sort">Null: most downloaded for an empty query, best match otherwise.</param>
    public static Uri SearchUrl(ModPlatform platform, string gameVersion, string query, int offset, ModSort? sort = null)
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
        var index = (sort ?? (trimmed.Length == 0 ? ModSort.Downloads : ModSort.Relevance)) switch
        {
            ModSort.Relevance => "relevance",
            ModSort.Newest => "newest",
            ModSort.Updated => "updated",
            _ => "downloads",
        };
        return new Uri(string.Create(
            CultureInfo.InvariantCulture,
            $"{ApiBase}/search?query={Uri.EscapeDataString(trimmed)}&facets={Escape(facets)}&index={index}&offset={offset}&limit={PageSize}"));
    }

    public static Uri VersionsUrl(string projectId, ModPlatform platform, string gameVersion) =>
        new($"{ApiBase}/project/{Uri.EscapeDataString(projectId)}/version?loaders={Escape(platform.Loaders)}&game_versions={Escape(new[] { gameVersion })}");

    public static readonly Uri VersionFilesUrl = new($"{ApiBase}/version_files");

    public static readonly Uri VersionFilesUpdateUrl = new($"{ApiBase}/version_files/update");

    public static Uri ProjectUrl(string projectId) => new($"{ApiBase}/project/{Uri.EscapeDataString(projectId)}");

    public static Uri ProjectsUrl(IEnumerable<string> projectIds) =>
        new($"{ApiBase}/projects?ids={Escape(projectIds.Order(StringComparer.Ordinal).ToArray())}");

    public async Task<ModSearchPage> SearchAsync(
        ModPlatform platform, string gameVersion, string query, int offset, ModSort? sort = null, CancellationToken cancellationToken = default)
    {
        await using var stream = await GetStreamAsync(SearchUrl(platform, gameVersion, query, offset, sort), cancellationToken).ConfigureAwait(false);
        return ParseSearch(stream);
    }

    /// <summary>The version to install, or null when the project has none for this server.</summary>
    public async Task<ModVersion?> GetBestVersionAsync(
        string projectId, ModPlatform platform, string gameVersion, CancellationToken cancellationToken = default)
    {
        await using var stream = await GetStreamAsync(VersionsUrl(projectId, platform, gameVersion), cancellationToken).ConfigureAwait(false);
        return ParseBestVersion(stream, platform, gameVersion);
    }

    /// <summary>Every version that runs on this loader and Minecraft version, newest first, for the details window.</summary>
    public async Task<IReadOnlyList<ModVersion>> GetVersionsAsync(
        string projectId, ModPlatform platform, string gameVersion, CancellationToken cancellationToken = default)
    {
        await using var stream = await GetStreamAsync(VersionsUrl(projectId, platform, gameVersion), cancellationToken).ConfigureAwait(false);
        return ParseVersions(stream, platform, gameVersion);
    }

    /// <summary>The project's long description, pictures and sides, for the details window.</summary>
    public async Task<ModProjectDetails> GetProjectDetailsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var stream = await GetStreamAsync(ProjectUrl(projectId), cancellationToken).ConfigureAwait(false);
        return ParseProjectDetails(stream);
    }

    public static ModProjectDetails ParseProjectDetails(Stream projectJson)
    {
        var project = JsonSerializer.Deserialize<ProjectDetailsDto>(projectJson)
            ?? throw new InvalidDataException("The Modrinth project is empty.");
        var gallery = (project.Gallery ?? [])
            .OrderByDescending(g => g.Featured)
            .Select(g => SafeHttps(g.Url))
            .OfType<Uri>()
            .ToList();
        return new ModProjectDetails(
            project.Id,
            string.IsNullOrWhiteSpace(project.Title) ? project.Id : project.Title,
            project.Description ?? string.Empty,
            project.Body ?? string.Empty,
            SafeHttps(project.IconUrl),
            gallery,
            project.ClientSide,
            project.ServerSide);
    }

    /// <summary>Compatible versions with a safe primary jar, newest first; the rest are left out.</summary>
    public static IReadOnlyList<ModVersion> ParseVersions(Stream versionsJson, ModPlatform platform, string gameVersion)
    {
        var versions = JsonSerializer.Deserialize<List<VersionDto>>(versionsJson)
            ?? throw new InvalidDataException("The Modrinth version list is empty.");
        var list = new List<ModVersion>();
        foreach (var version in versions
            .Where(v => v.GameVersions.Contains(gameVersion, StringComparer.Ordinal)
                && v.Loaders.Any(l => platform.Loaders.Contains(l, StringComparer.Ordinal))
                && v.Files.Count > 0)
            .OrderByDescending(v => v.DatePublished))
        {
            try
            {
                list.Add(ToModVersion(version));
            }
            catch (Exception e) when (e is InvalidDataException or UriFormatException)
            {
                // A version without a usable jar cannot be offered.
            }
        }

        return list;
    }

    private static Uri? SafeHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;

    /// <summary>A project's display name, used for dependencies, which Modrinth names only by id.</summary>
    public async Task<string> GetProjectTitleAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var stream = await GetStreamAsync(ProjectUrl(projectId), cancellationToken).ConfigureAwait(false);
        var project = await JsonSerializer.DeserializeAsync<ProjectDto>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(project?.Title) ? projectId : project.Title;
    }

    /// <summary>
    /// The Modrinth projects that published these files (SHA-512 hashes). Files Modrinth does not know are left out.
    /// </summary>
    public async Task<IReadOnlySet<string>> IdentifyProjectsAsync(IReadOnlyCollection<string> sha512Hashes, CancellationToken cancellationToken = default) =>
        (await IdentifyFilesAsync(sha512Hashes, cancellationToken).ConfigureAwait(false)).Values.ToHashSet(StringComparer.Ordinal);

    /// <summary>The project that published each file, by SHA-512 hash. Files Modrinth does not know are left out.</summary>
    public async Task<IReadOnlyDictionary<string, string>> IdentifyFilesAsync(IReadOnlyCollection<string> sha512Hashes, CancellationToken cancellationToken = default) =>
        (await IdentifyVersionsAsync(sha512Hashes, cancellationToken).ConfigureAwait(false))
            .ToDictionary(v => v.Key, v => v.Value.ProjectId, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The project and incompatible projects of each file's version, by SHA-512 hash. Files Modrinth does not know
    /// are left out.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IdentifiedVersion>> IdentifyVersionsAsync(IReadOnlyCollection<string> sha512Hashes, CancellationToken cancellationToken = default)
    {
        if (sha512Hashes.Count == 0)
        {
            return new Dictionary<string, IdentifiedVersion>();
        }

        var body = JsonSerializer.Serialize(new { hashes = sha512Hashes, algorithm = "sha512" });
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, VersionFilesUrl) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") },
            cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return ParseIdentifiedVersions(stream);
    }

    /// <summary>
    /// The newest release for this loader and Minecraft version of each file's project, by the file's SHA-512 hash.
    /// Files Modrinth does not know, or whose project has no such release, are left out; a file that is already the
    /// newest comes back as itself.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ModVersion>> GetLatestVersionsAsync(
        IReadOnlyCollection<string> sha512Hashes, ModPlatform platform, string gameVersion, CancellationToken cancellationToken = default)
    {
        if (sha512Hashes.Count == 0)
        {
            return new Dictionary<string, ModVersion>();
        }

        var body = JsonSerializer.Serialize(new
        {
            hashes = sha512Hashes,
            algorithm = "sha512",
            loaders = platform.Loaders,
            game_versions = new[] { gameVersion },
            version_types = new[] { "release" },
        });
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, VersionFilesUpdateUrl) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") },
            cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return ParseLatestVersions(stream);
    }

    /// <summary>Versions whose primary file is not a safe HTTPS jar with a SHA-512 are left out.</summary>
    public static IReadOnlyDictionary<string, ModVersion> ParseLatestVersions(Stream updateJson)
    {
        var versions = JsonSerializer.Deserialize<Dictionary<string, VersionDto>>(updateJson) ?? [];
        var latest = new Dictionary<string, ModVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hash, version) in versions)
        {
            try
            {
                if (version.Files.Count > 0)
                {
                    latest[hash] = ToModVersion(version);
                }
            }
            catch (Exception e) when (e is InvalidDataException or UriFormatException)
            {
                // One odd file must not hide the other updates.
            }
        }

        return latest;
    }

    public static IReadOnlySet<string> ParseIdentifiedProjects(Stream versionFilesJson) =>
        ParseIdentifiedFiles(versionFilesJson).Values.ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> ParseIdentifiedFiles(Stream versionFilesJson) =>
        ParseIdentifiedVersions(versionFilesJson).ToDictionary(v => v.Key, v => v.Value.ProjectId, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, IdentifiedVersion> ParseIdentifiedVersions(Stream versionFilesJson)
    {
        var versions = JsonSerializer.Deserialize<Dictionary<string, IdentifiedDto>>(versionFilesJson) ?? [];
        return versions.ToDictionary(
            v => v.Key,
            v => new IdentifiedVersion(v.Value.ProjectId, IncompatibleIds(v.Value.Dependencies)) { VersionId = v.Value.Id },
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The title and server support of each project. Projects Modrinth no longer has are left out.</summary>
    public async Task<IReadOnlyList<ModProject>> GetProjectsAsync(IReadOnlyCollection<string> projectIds, CancellationToken cancellationToken = default)
    {
        if (projectIds.Count == 0)
        {
            return [];
        }

        await using var stream = await GetStreamAsync(ProjectsUrl(projectIds), cancellationToken).ConfigureAwait(false);
        return ParseProjects(stream);
    }

    public static IReadOnlyList<ModProject> ParseProjects(Stream projectsJson)
    {
        var projects = JsonSerializer.Deserialize<List<ProjectDto>>(projectsJson) ?? [];
        return projects
            .Where(p => !string.IsNullOrEmpty(p.Id))
            .Select(p => new ModProject(p.Id!, string.IsNullOrWhiteSpace(p.Title) ? p.Id! : p.Title, p.ServerSide == "unsupported"))
            .ToList();
    }

    public static ModSearchPage ParseSearch(Stream searchJson)
    {
        var result = JsonSerializer.Deserialize<SearchDto>(searchJson)
            ?? throw new InvalidDataException("The Modrinth search result is empty.");

        var hits = result.Hits
            .Select(h => new ModSearchHit(h.ProjectId, h.Slug ?? h.ProjectId, h.Title, h.Description ?? string.Empty, h.Author ?? string.Empty, h.Downloads, h.ProjectType ?? "mod")
            {
                IconUrl = SafeHttps(h.IconUrl),
                Updated = h.DateModified,
                ServerSide = h.ServerSide,
            })
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
        return best is null ? null : ToModVersion(best);
    }

    /// <summary>The version's primary file, which must be an HTTPS .jar with a SHA-512 checksum.</summary>
    private static ModVersion ToModVersion(VersionDto best)
    {
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
        return new ModVersion(best.ProjectId, best.Id, best.VersionNumber, file.FileName, url, sha512, required, IncompatibleIds(best.Dependencies))
        {
            VersionType = best.VersionType,
            Published = best.DatePublished,
        };
    }

    /// <summary>
    /// The projects a version is marked as not working with. Entries naming only a version, not a project, are left
    /// out: WaNut matches installed mods by project.
    /// </summary>
    private static IReadOnlyList<string> IncompatibleIds(List<DependencyDto>? dependencies) =>
        (dependencies ?? [])
            .Where(d => d.DependencyType == "incompatible" && !string.IsNullOrEmpty(d.ProjectId))
            .Select(d => d.ProjectId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private async Task<Stream> GetStreamAsync(Uri url, CancellationToken cancellationToken)
    {
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends with rate-limit retries; a failure status throws, as HttpClient.GetStreamAsync does.</summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        var response = await RateLimitRetry.SendAsync(http, createRequest, delay, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new HttpRequestException($"Modrinth answered {(int)response.StatusCode} ({response.ReasonPhrase}).", null, response.StatusCode);
        }

        return response;
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
        [property: JsonPropertyName("project_type")] string? ProjectType,
        [property: JsonPropertyName("icon_url")] string? IconUrl = null,
        [property: JsonPropertyName("date_modified")] DateTimeOffset? DateModified = null,
        [property: JsonPropertyName("server_side")] string? ServerSide = null);

    private sealed record IdentifiedDto(
        [property: JsonPropertyName("project_id")] string ProjectId,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("dependencies")] List<DependencyDto>? Dependencies);

    private sealed record ProjectDto(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("server_side")] string? ServerSide);

    private sealed record ProjectDetailsDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("icon_url")] string? IconUrl,
        [property: JsonPropertyName("gallery")] List<GalleryDto>? Gallery,
        [property: JsonPropertyName("client_side")] string? ClientSide,
        [property: JsonPropertyName("server_side")] string? ServerSide);

    private sealed record GalleryDto(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("featured")] bool Featured);

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
