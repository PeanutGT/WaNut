using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaNut.Core.Versions;

/// <summary>The Fabric server launcher for one loader version, before it is set up for a game version.</summary>
public sealed record FabricLauncher(string LoaderVersion, string InstallerVersion, Uri JarUrl, string Sha256);

/// <summary>
/// Reads Fabric versions from Fabric Meta and locates the server launcher on Fabric's Maven.
/// </summary>
/// <remarks>
/// Fabric Meta's ready-made server jar has no published checksum. It is the Maven
/// fabric-installer-&lt;v&gt;-server.jar plus an install.properties file, so WaNut downloads that jar from Maven,
/// checks it against Maven's .sha256 file, and adds install.properties itself (<see cref="WriteInstallProperties"/>).
/// </remarks>
public sealed class FabricClient(HttpClient http)
{
    public static readonly Uri GameVersionsUrl = new("https://meta.fabricmc.net/v2/versions/game");
    public static readonly Uri LoaderVersionsUrl = new("https://meta.fabricmc.net/v2/versions/loader");
    public static readonly Uri InstallerVersionsUrl = new("https://meta.fabricmc.net/v2/versions/installer");

    public const string LauncherFileName = "fabric-server-launch.jar";

    public static Uri ServerLauncherUrl(string installerVersion)
    {
        var v = Uri.EscapeDataString(installerVersion);
        return new($"https://maven.fabricmc.net/net/fabricmc/fabric-installer/{v}/fabric-installer-{v}-server.jar");
    }

    /// <summary>Game versions Fabric marks stable (releases, not snapshots).</summary>
    public async Task<IReadOnlySet<string>> GetStableGameVersionsAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = await http.GetStreamAsync(GameVersionsUrl, cancellationToken).ConfigureAwait(false);
        return ParseStableGameVersions(stream);
    }

    public async Task<FabricLauncher> GetLatestLauncherAsync(CancellationToken cancellationToken = default)
    {
        string loader;
        await using (var stream = await http.GetStreamAsync(LoaderVersionsUrl, cancellationToken).ConfigureAwait(false))
        {
            loader = ParseLatestStable(stream, "loader");
        }

        string installer;
        await using (var stream = await http.GetStreamAsync(InstallerVersionsUrl, cancellationToken).ConfigureAwait(false))
        {
            installer = ParseLatestStable(stream, "installer");
        }

        var jarUrl = ServerLauncherUrl(installer);
        var sha256 = ParseChecksumFile(await http.GetStringAsync(new Uri(jarUrl.AbsoluteUri + ".sha256"), cancellationToken).ConfigureAwait(false));
        return new FabricLauncher(loader, installer, jarUrl, sha256);
    }

    public static IReadOnlySet<string> ParseStableGameVersions(Stream json)
    {
        var versions = JsonSerializer.Deserialize<List<VersionDto>>(json)
            ?? throw new InvalidDataException("The Fabric game version list is empty.");
        return versions.Where(v => v.Stable).Select(v => v.Version).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The first stable entry; Fabric Meta lists newest first.</summary>
    public static string ParseLatestStable(Stream json, string what)
    {
        var versions = JsonSerializer.Deserialize<List<VersionDto>>(json)
            ?? throw new InvalidDataException($"The Fabric {what} version list is empty.");
        return versions.FirstOrDefault(v => v.Stable)?.Version
            ?? throw new InvalidDataException($"Fabric lists no stable {what} version.");
    }

    /// <summary>Maven checksum files hold the bare hex value, sometimes followed by a file name.</summary>
    public static string ParseChecksumFile(string text)
    {
        var hex = text.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (hex.Length != 64 || !hex.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Fabric's Maven returned an unreadable SHA-256 checksum.");
        }

        return hex.ToLowerInvariant();
    }

    /// <summary>
    /// Tells the launcher which loader and game version to set up, the same file Fabric Meta puts in its server jar.
    /// </summary>
    public static void WriteInstallProperties(string launcherJar, string loaderVersion, string gameVersion)
    {
        using var zip = ZipFile.Open(launcherJar, ZipArchiveMode.Update);
        zip.GetEntry("install.properties")?.Delete();
        var entry = zip.CreateEntry("install.properties");
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write($"fabric-loader-version={loaderVersion}\ngame-version={gameVersion}\n");
    }

    private sealed record VersionDto(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("stable")] bool Stable);
}
