using System.Globalization;
using System.Text.Json;

namespace WaNut.Core.Versions;

/// <summary>
/// Reads NeoForge versions from NeoForged's Maven and locates their installers.
/// </summary>
/// <remarks>
/// A NeoForge version names its Minecraft version: 21.1.x is for 1.21.1 and 21.0.x for 1.21. From the year-based
/// Minecraft versions on it starts with the Minecraft version itself (26.1.0.x for 26.1). NeoForge for 1.20.1 is a
/// separate legacy artifact and is not offered. New Minecraft versions often only have beta builds for a while, so a
/// beta is used when no stable build exists.
/// </remarks>
public sealed class NeoForgeClient(HttpClient http)
{
    public static readonly Uri VersionsUrl = new("https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge");

    private const string BetaSuffix = "-beta";

    public static Uri InstallerUrl(string version)
    {
        var v = Uri.EscapeDataString(version);
        return new($"https://maven.neoforged.net/releases/net/neoforged/neoforge/{v}/neoforge-{v}-installer.jar");
    }

    public async Task<IReadOnlySet<string>> GetGameVersionsAsync(CancellationToken cancellationToken = default) =>
        (await GetBestAsync(cancellationToken).ConfigureAwait(false)).Keys.ToHashSet(StringComparer.Ordinal);

    /// <param name="loaderVersion">A NeoForge version a modpack asks for; null for the newest one.</param>
    public async Task<ForgeInstaller> GetInstallerAsync(string gameVersion, string? loaderVersion = null, CancellationToken cancellationToken = default)
    {
        if (loaderVersion is null)
        {
            var best = await GetBestAsync(cancellationToken).ConfigureAwait(false);
            if (!best.TryGetValue(gameVersion, out loaderVersion))
            {
                throw new InvalidDataException($"NeoForge has no build for Minecraft {gameVersion}.");
            }
        }

        var url = InstallerUrl(loaderVersion);
        var sha1 = MavenChecksum.Parse(
            await http.GetStringAsync(new Uri(url.AbsoluteUri + ".sha1"), cancellationToken).ConfigureAwait(false), MavenChecksum.Sha1Length);
        return new ForgeInstaller(ServerLoader.NeoForge, loaderVersion, loaderVersion, url, sha1);
    }

    /// <summary>The newest stable NeoForge version for each Minecraft version, or the newest beta when none is stable.</summary>
    public static IReadOnlyDictionary<string, string> ParseBestVersions(Stream json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("versions", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("NeoForge's version list is empty.");
        }

        var best = new Dictionary<string, (string Version, bool Beta, int[] Numbers)>(StringComparer.Ordinal);
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } version
                || GameVersionOf(version) is not { } game || Numbers(version) is not { } numbers)
            {
                continue;
            }

            var beta = version.EndsWith(BetaSuffix, StringComparison.Ordinal);
            if (!best.TryGetValue(game, out var current)
                || (current.Beta && !beta)
                || (current.Beta == beta && Compare(numbers, current.Numbers) > 0))
            {
                best[game] = (version, beta, numbers);
            }
        }

        return best.ToDictionary(p => p.Key, p => p.Value.Version, StringComparer.Ordinal);
    }

    /// <summary>
    /// The Minecraft version a NeoForge version is for: "21.1.209" is 1.21.1, "21.0.167" is 1.21, "26.1.0.5-beta" is
    /// 26.1 and "26.1.2.3" is 26.1.2. Null for anything else, such as April Fools' builds.
    /// </summary>
    public static string? GameVersionOf(string version)
    {
        var numbers = Numbers(version);
        if (numbers is null)
        {
            return null;
        }

        return numbers switch
        {
            [>= 20 and < 25, var minor, _] when numbers[0] > 20 || minor >= 2 => minor == 0 ? $"1.{numbers[0]}" : $"1.{numbers[0]}.{minor}",
            [>= 25, var drop, var hotfix, _] => hotfix == 0 ? $"{numbers[0]}.{drop}" : $"{numbers[0]}.{drop}.{hotfix}",
            _ => null,
        };
    }

    private static int[]? Numbers(string version)
    {
        var plain = version.EndsWith(BetaSuffix, StringComparison.Ordinal) ? version[..^BetaSuffix.Length] : version;
        var parts = plain.Split('.');
        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        return numbers;
    }

    private static int Compare(int[] a, int[] b)
    {
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            if (a[i] != b[i])
            {
                return a[i].CompareTo(b[i]);
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    private async Task<IReadOnlyDictionary<string, string>> GetBestAsync(CancellationToken cancellationToken)
    {
        await using var stream = await http.GetStreamAsync(VersionsUrl, cancellationToken).ConfigureAwait(false);
        return ParseBestVersions(stream);
    }
}
