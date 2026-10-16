using System.Text.Json;

namespace WaNut.Core.Versions;

/// <summary>
/// Reads Forge versions from Forge's promotions list and locates installers on Forge's Maven.
/// </summary>
/// <remarks>
/// Each Minecraft version has a "recommended" and a "latest" promotion. WaNut takes the recommended build and falls
/// back to the latest one for versions that never got a recommended build.
/// </remarks>
public sealed class ForgeClient(HttpClient http)
{
    public static readonly Uri PromotionsUrl = new("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json");

    public static Uri InstallerUrl(string fullVersion)
    {
        var v = Uri.EscapeDataString(fullVersion);
        return new($"https://maven.minecraftforge.net/net/minecraftforge/forge/{v}/forge-{v}-installer.jar");
    }

    public async Task<IReadOnlySet<string>> GetGameVersionsAsync(CancellationToken cancellationToken = default) =>
        (await GetPromotedAsync(cancellationToken).ConfigureAwait(false)).Keys.ToHashSet(StringComparer.Ordinal);

    /// <param name="loaderVersion">A Forge version a modpack asks for; null for the promoted one.</param>
    public async Task<ForgeInstaller> GetInstallerAsync(string gameVersion, string? loaderVersion = null, CancellationToken cancellationToken = default)
    {
        if (loaderVersion is null)
        {
            var promoted = await GetPromotedAsync(cancellationToken).ConfigureAwait(false);
            if (!promoted.TryGetValue(gameVersion, out loaderVersion))
            {
                throw new InvalidDataException($"Forge has no build for Minecraft {gameVersion}.");
            }
        }

        var full = $"{gameVersion}-{loaderVersion}";
        var url = InstallerUrl(full);
        var sha1 = MavenChecksum.Parse(
            await http.GetStringAsync(new Uri(url.AbsoluteUri + ".sha1"), cancellationToken).ConfigureAwait(false), MavenChecksum.Sha1Length);
        return new ForgeInstaller(ServerLoader.Forge, loaderVersion, full, url, sha1);
    }

    /// <summary>The Forge version to use for each Minecraft version: recommended first, then latest.</summary>
    public static IReadOnlyDictionary<string, string> ParsePromotions(Stream json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("promos", out var promos) || promos.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Forge's promotions list has no promos.");
        }

        var latest = new Dictionary<string, string>(StringComparer.Ordinal);
        var recommended = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var promo in promos.EnumerateObject())
        {
            if (promo.Value.ValueKind != JsonValueKind.String || promo.Value.GetString() is not { Length: > 0 } version)
            {
                continue;
            }

            var dash = promo.Name.LastIndexOf('-');
            if (dash <= 0)
            {
                continue;
            }

            var game = promo.Name[..dash];
            switch (promo.Name[(dash + 1)..])
            {
                case "recommended":
                    recommended[game] = version;
                    break;
                case "latest":
                    latest[game] = version;
                    break;
            }
        }

        foreach (var (game, version) in recommended)
        {
            latest[game] = version;
        }

        return latest;
    }

    private async Task<IReadOnlyDictionary<string, string>> GetPromotedAsync(CancellationToken cancellationToken)
    {
        await using var stream = await http.GetStreamAsync(PromotionsUrl, cancellationToken).ConfigureAwait(false);
        return ParsePromotions(stream);
    }
}
