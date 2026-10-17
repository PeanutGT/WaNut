using System.Text.Json.Nodes;
using WaNut.Core.Downloads;

namespace WaNut.Core.Tunnels;

/// <summary>
/// Downloads playit's official, code-signed Windows agent (playitd) from its GitHub releases, checked against the
/// SHA-256 digest GitHub publishes for the file.
/// </summary>
public sealed class PlayitAgentDownloader(HttpClient http, FileDownloader downloader, PlayitFiles files)
{
    public static readonly Uri LatestReleaseUrl = new("https://api.github.com/repos/playit-cloud/playit-agent/releases/latest");

    public const string AssetName = "playit-windows-x86_64-signed.exe";

    /// <summary>The agent to run: the one already downloaded, otherwise the latest release.</summary>
    public async Task<string> EnsureInstalledAsync(IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (files.FindAgentExe() is { } existing)
        {
            return existing;
        }

        var json = await http.GetStringAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);
        var (version, url, sha256) = ParseRelease(JsonNode.Parse(json));
        var target = files.AgentExe(version);
        await downloader.DownloadAsync(url, target, ExpectedHash.Sha256(sha256), progress, cancellationToken).ConfigureAwait(false);
        return target;
    }

    public static (string Version, Uri Url, string Sha256) ParseRelease(JsonNode? release)
    {
        var version = release?["tag_name"]?.GetValue<string>();
        var asset = release?["assets"]?.AsArray().OfType<JsonNode>().FirstOrDefault(a => a["name"]?.GetValue<string>() == AssetName);
        var url = asset?["browser_download_url"]?.GetValue<string>();
        var digest = asset?["digest"]?.GetValue<string>();
        const string Prefix = "sha256:";
        if (version is null || url is null || digest is null || !digest.StartsWith(Prefix, StringComparison.Ordinal)
            || version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException("The playit release does not list a signed Windows agent with a SHA-256 digest.");
        }

        return (version, new Uri(url), digest[Prefix.Length..]);
    }
}
