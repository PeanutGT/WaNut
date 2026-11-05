using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaNut.Core.Java;

/// <summary>
/// Finds portable Eclipse Temurin JREs through the Adoptium API.
/// </summary>
public sealed class AdoptiumClient(HttpClient http)
{
    /// <param name="os">Adoptium's name: "windows" (a .zip) or "linux" (a .tar.gz).</param>
    /// <param name="architecture">Adoptium's name: "x64" or "aarch64".</param>
    public static Uri LatestJreUrl(int major, string os = "windows", string architecture = "x64") =>
        new($"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture={architecture}&image_type=jre&os={os}&vendor=eclipse");

    public async Task<JavaPackage> GetLatestJreAsync(int major, CancellationToken cancellationToken = default) =>
        await GetLatestJreAsync(major, "windows", "x64", cancellationToken).ConfigureAwait(false);

    /// <summary>A JRE for another machine, such as the user's Linux VPS.</summary>
    public async Task<JavaPackage> GetLatestJreAsync(int major, string os, string architecture, CancellationToken cancellationToken = default)
    {
        await using var stream = await http.GetStreamAsync(LatestJreUrl(major, os, architecture), cancellationToken).ConfigureAwait(false);
        return ParseLatest(major, stream, os, architecture);
    }

    public static JavaPackage ParseLatest(int major, Stream json, string os = "windows", string architecture = "x64")
    {
        var assets = JsonSerializer.Deserialize<List<AssetDto>>(json) ?? [];
        var extension = os == "windows" ? ".zip" : ".tar.gz";

        var asset = assets.FirstOrDefault(a =>
            a.Binary is { ImageType: "jre" }
            && a.Binary.Os == os
            && a.Binary.Architecture == architecture
            && a.Binary.Package is not null
            && a.Binary.Package.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Adoptium has no {os} {architecture} JRE {extension} for Java {major}.");

        var package = asset.Binary!.Package!;
        return new JavaPackage(major, asset.ReleaseName, new Uri(package.Link), package.Checksum, package.Size);
    }

    private sealed record AssetDto(
        [property: JsonPropertyName("binary")] BinaryDto? Binary,
        [property: JsonPropertyName("release_name")] string ReleaseName);

    private sealed record BinaryDto(
        [property: JsonPropertyName("architecture")] string Architecture,
        [property: JsonPropertyName("image_type")] string ImageType,
        [property: JsonPropertyName("os")] string Os,
        [property: JsonPropertyName("package")] PackageDto? Package);

    private sealed record PackageDto(
        [property: JsonPropertyName("checksum")] string Checksum,
        [property: JsonPropertyName("link")] string Link,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("size")] long Size);
}
