using System.Text.Json;
using System.Text.RegularExpressions;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Mods;
using WaNut.Core.Settings;

namespace WaNut.Core.Profiling;

/// <summary>Where a server gets the spark profiler from.</summary>
public enum SparkSource
{
    /// <summary>Vanilla loads neither mods nor plugins.</summary>
    None,

    /// <summary>Paper 1.21 and later ship spark themselves.</summary>
    BuiltIn,

    /// <summary>Fabric, Forge and NeoForge: the spark mod from Modrinth.</summary>
    Modrinth,

    /// <summary>Older Paper: the Bukkit plugin from spark's own download API (spark is not on Modrinth for Paper).</summary>
    Official,
}

/// <summary>
/// The spark profiler (spark.lucko.me): which servers can run it, installing it, the console command that profiles
/// for a while, and finding the report link spark prints when it is done.
/// </summary>
public sealed partial class Spark(ModLibrary mods, HttpClient http, FileDownloader downloader)
{
    /// <summary>spark's Modrinth project.</summary>
    public const string ModrinthProjectId = "l6YH9Als";

    public const string Title = "spark";

    /// <summary>How long one profile runs.</summary>
    public const int ProfileSeconds = 30;

    /// <summary>The latest build of each platform, with its SHA-1.</summary>
    public static readonly Uri DownloadIndexUrl = new("https://sparkapi.lucko.me/download");

    private static readonly Version BuiltIntoPaperSince = new(1, 21, 0);

    /// <summary>Profiles for <see cref="ProfileSeconds"/>, then uploads the report and prints its link.</summary>
    public static string ProfileCommand { get; } = $"spark profiler start --timeout {ProfileSeconds}";

    public static SparkSource SourceFor(ServerType type, string minecraftVersion) => type switch
    {
        ServerType.Fabric or ServerType.Forge or ServerType.NeoForge => SparkSource.Modrinth,
        ServerType.Paper => GameFeatures.ParseVersion(minecraftVersion) >= BuiltIntoPaperSince ? SparkSource.BuiltIn : SparkSource.Official,
        _ => SparkSource.None,
    };

    /// <summary>True when spark is built in, or a spark jar is in the mods or plugins folder (also one copied in by hand).</summary>
    public static bool IsInstalled(string instanceDir, ServerType type, string minecraftVersion)
    {
        var source = SourceFor(type, minecraftVersion);
        if (source == SparkSource.BuiltIn)
        {
            return true;
        }

        if (source == SparkSource.None || ModPlatform.For(type)?.FolderIn(instanceDir) is not { } folder || !Directory.Exists(folder))
        {
            return false;
        }

        return Directory.EnumerateFiles(folder, "*.jar").Select(Path.GetFileName).Any(IsSparkJarName);
    }

    /// <summary>"spark-1.10.195-fabric.jar", "spark-forge.jar"; not "sparkly-paintings.jar".</summary>
    public static bool IsSparkJarName(string? fileName) => fileName is not null && SparkJarName().IsMatch(fileName);

    /// <summary>The report link in a console line such as "[⚡] https://spark.lucko.me/AbC123xyz", or null.</summary>
    public static Uri? FindReportUrl(string line) =>
        ReportUrl().Match(line) is { Success: true } match ? new Uri(match.Value) : null;

    /// <summary>
    /// Installs spark for the server. Only call while it is stopped. Modrinth files are checked against their
    /// SHA-512, the official Bukkit jar against the SHA-1 its download API lists.
    /// </summary>
    /// <exception cref="NoCompatibleVersionException">Modrinth has no spark for this loader and version.</exception>
    public async Task InstallAsync(
        string instanceDir, ServerType type, string minecraftVersion, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        switch (SourceFor(type, minecraftVersion))
        {
            case SparkSource.Modrinth:
                // Nothing else is installed with spark, so Modrinth's incompatibility marks need no question.
                await mods.InstallAsync(
                    instanceDir, ModPlatform.For(type)!, minecraftVersion, ModrinthProjectId, Title, allowIncompatible: true, progress, cancellationToken).ConfigureAwait(false);
                break;

            case SparkSource.Official:
                var (url, fileName, sha1) = await GetBukkitBuildAsync(cancellationToken).ConfigureAwait(false);
                var fileProgress = progress is null ? null : new SyncProgress<DownloadProgress>(p => progress.Report(p.Fraction ?? 0));
                await downloader.DownloadAsync(
                    url, Path.Combine(ModPlatform.For(type)!.FolderIn(instanceDir), fileName), ExpectedHash.Sha1(sha1), fileProgress, cancellationToken).ConfigureAwait(false);
                progress?.Report(1);
                break;

            default:
                throw new InvalidOperationException($"{type} {minecraftVersion} does not need or cannot load spark.");
        }
    }

    private async Task<(Uri Url, string FileName, string Sha1)> GetBukkitBuildAsync(CancellationToken cancellationToken)
    {
        await using var stream = await http.GetStreamAsync(DownloadIndexUrl, cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var bukkit = json.RootElement.GetProperty("bukkit");
        var fileName = bukkit.GetProperty("fileName").GetString();
        var url = bukkit.GetProperty("url").GetString();
        var sha1 = bukkit.GetProperty("sha1").GetString();
        if (!IsSparkJarName(fileName) || !ModrinthClient.IsSafeJarName(fileName!)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || sha1 is not { Length: 40 } || !sha1.All(char.IsAsciiHexDigit))
        {
            throw new InvalidDataException("spark's download list has no usable Bukkit build.");
        }

        return (uri, fileName!, sha1);
    }

    [GeneratedRegex(@"^spark([-_].*)?\.jar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SparkJarName();

    [GeneratedRegex(@"https://spark\.lucko\.me/[A-Za-z0-9]+")]
    private static partial Regex ReportUrl();
}
