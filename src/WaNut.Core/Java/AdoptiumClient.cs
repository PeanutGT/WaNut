using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaNut.Core.Java;

/// <summary>
/// Finds portable Eclipse Temurin JREs through the Adoptium API.
/// </summary>
public sealed class AdoptiumClient(HttpClient http)
{
    public static Uri LatestJreUrl(int major) =>
        new($"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse");

    public async Task<JavaPackage> GetLatestJreAsync(int major, CancellationToken cancellationToken = default)
    {
        await using var stream = await http.GetStreamAsync(LatestJreUrl(major), cancellationToken).ConfigureAwait(false);
        return ParseLatest(major, stream);
    }

    public static JavaPackage ParseLatest(int major, Stream json)
    {
        var assets = JsonSerializer.Deserialize<List<AssetDto>>(json) ?? [];

        var asset = assets.FirstOrDefault(a =>
            a.Binary is { ImageType: "jre", Os: "windows", Architecture: "x64" }
            && a.Binary.Package is not null
            && a.Binary.Package.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Adoptium has no Windows x64 JRE zip for Java {major}.");

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
