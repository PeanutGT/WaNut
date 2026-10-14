using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaNut.Core.Versions;

/// <summary>A Paper server build from the Fill v3 API.</summary>
public sealed record PaperBuild(string VersionId, int Id, string Channel, Uri JarUrl, string Sha256, long Size);

/// <summary>
/// Reads Paper versions and builds from PaperMC's Fill v3 API (the v2 API is being retired).
/// </summary>
public sealed class PaperClient(HttpClient http)
{
    private const string ServerDownloadKey = "server:default";

    public static readonly Uri ProjectUrl = new("https://fill.papermc.io/v3/projects/paper");

    public static Uri BuildsUrl(string versionId) =>
        new($"https://fill.papermc.io/v3/projects/paper/versions/{Uri.EscapeDataString(versionId)}/builds");

    /// <summary>Release version ids that Paper has builds for. Pre-releases and release candidates are left out.</summary>
    public async Task<IReadOnlySet<string>> GetVersionIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = await http.GetStreamAsync(ProjectUrl, cancellationToken).ConfigureAwait(false);
        return ParseVersionIds(stream);
    }

    public async Task<PaperBuild> GetBestBuildAsync(string versionId, CancellationToken cancellationToken = default)
    {
        await using var stream = await http.GetStreamAsync(BuildsUrl(versionId), cancellationToken).ConfigureAwait(false);
        return ParseBestBuild(versionId, stream);
    }

    public static IReadOnlySet<string> ParseVersionIds(Stream projectJson)
    {
        var project = JsonSerializer.Deserialize<ProjectDto>(projectJson)
            ?? throw new InvalidDataException("The Paper project data is empty.");

        // Ids such as "1.21.11-rc3", "1.21.9-pre4" and "26.3-rc-3" are test versions.
        return project.Versions.Values
            .SelectMany(ids => ids)
            .Where(id => !id.Contains('-', StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The newest STABLE build. Only when a version has none yet, the newest BETA build, then the newest of any kind.
    /// </summary>
    public static PaperBuild ParseBestBuild(string versionId, Stream buildsJson)
    {
        var builds = JsonSerializer.Deserialize<List<BuildDto>>(buildsJson)
            ?? throw new InvalidDataException($"The Paper builds for {versionId} are empty.");

        var withJar = builds.Where(b => b.Downloads?.ContainsKey(ServerDownloadKey) == true).ToList();
        var best = withJar.Where(b => b.Channel == "STABLE").MaxBy(b => b.Id)
            ?? withJar.Where(b => b.Channel == "BETA").MaxBy(b => b.Id)
            ?? withJar.MaxBy(b => b.Id)
            ?? throw new InvalidDataException($"Paper has no server download for {versionId}.");

        var download = best.Downloads![ServerDownloadKey];
        var url = new Uri(download.Url);
        if (url.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException($"The Paper download for {versionId} is not HTTPS.");
        }

        var sha256 = download.Checksums?.Sha256
            ?? throw new InvalidDataException($"The Paper download for {versionId} has no SHA-256 checksum.");
        return new PaperBuild(versionId, best.Id, best.Channel, url, sha256, download.Size);
    }

    private sealed record ProjectDto(
        [property: JsonPropertyName("versions")] Dictionary<string, List<string>> Versions);

    private sealed record BuildDto(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("channel")] string Channel,
        [property: JsonPropertyName("downloads")] Dictionary<string, DownloadDto>? Downloads);

    private sealed record DownloadDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("checksums")] ChecksumsDto? Checksums,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("url")] string Url);

    private sealed record ChecksumsDto(
        [property: JsonPropertyName("sha256")] string? Sha256);
}
