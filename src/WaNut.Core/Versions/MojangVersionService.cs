using System.Text.Json;
using System.Text.Json.Serialization;
using WaNut.Core.Downloads;
using WaNut.Core.Java;

namespace WaNut.Core.Versions;

/// <summary>
/// Reads Minecraft versions and server downloads from Mojang's official version manifest.
/// </summary>
public sealed class MojangVersionService(HttpClient http, FileDownloader downloader)
{
    public static readonly Uri ManifestUrl = new("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");

    /// <summary>The oldest version WaNut offers (blueprint decision 11).</summary>
    public const string OldestSupportedVersion = "1.16.5";

    public async Task<IReadOnlyList<GameVersion>> GetReleasesAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = await http.GetStreamAsync(ManifestUrl, cancellationToken).ConfigureAwait(false);
        return ParseReleases(stream);
    }

    public async Task<VanillaServerInfo> GetServerInfoAsync(GameVersion version, CancellationToken cancellationToken = default)
    {
        var bytes = await downloader.DownloadBytesAsync(version.DetailsUrl, ExpectedHash.Sha1(version.DetailsSha1), cancellationToken).ConfigureAwait(false);
        return ParseServerInfo(version.Id, bytes);
    }

    /// <summary>
    /// Returns full releases from newest to oldest, stopping at <see cref="OldestSupportedVersion"/>.
    /// </summary>
    public static IReadOnlyList<GameVersion> ParseReleases(Stream manifestJson)
    {
        var manifest = JsonSerializer.Deserialize<ManifestDto>(manifestJson)
            ?? throw new InvalidDataException("The Minecraft version manifest is empty.");

        var releases = manifest.Versions
            .Where(v => v.Type == "release")
            .Select(v => new GameVersion(v.Id, v.ReleaseTime, new Uri(v.Url), v.Sha1))
            .OrderByDescending(v => v.ReleaseTime)
            .ToList();

        var oldest = releases.FindIndex(v => v.Id == OldestSupportedVersion);
        return oldest >= 0 ? releases.Take(oldest + 1).ToList() : releases;
    }

    public static VanillaServerInfo ParseServerInfo(string versionId, ReadOnlySpan<byte> detailsJson)
    {
        var details = JsonSerializer.Deserialize<VersionDetailsDto>(detailsJson)
            ?? throw new InvalidDataException($"The details for Minecraft {versionId} are empty.");

        var server = details.Downloads?.Server
            ?? throw new InvalidDataException($"Minecraft {versionId} has no official server download.");

        var javaMajor = details.JavaVersion?.MajorVersion ?? JavaVersionResolver.FallbackFor(versionId);
        return new VanillaServerInfo(versionId, new Uri(server.Url), server.Sha1, server.Size, javaMajor);
    }

    private sealed record ManifestDto(
        [property: JsonPropertyName("versions")] IReadOnlyList<ManifestVersionDto> Versions);

    private sealed record ManifestVersionDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("sha1")] string Sha1,
        [property: JsonPropertyName("releaseTime")] DateTimeOffset ReleaseTime);

    private sealed record VersionDetailsDto(
        [property: JsonPropertyName("downloads")] DownloadsDto? Downloads,
        [property: JsonPropertyName("javaVersion")] JavaVersionDto? JavaVersion);

    private sealed record DownloadsDto(
        [property: JsonPropertyName("server")] DownloadDto? Server);

    private sealed record DownloadDto(
        [property: JsonPropertyName("sha1")] string Sha1,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("url")] string Url);

    private sealed record JavaVersionDto(
        [property: JsonPropertyName("majorVersion")] int MajorVersion);
}
