using Velopack;
using Velopack.Sources;

namespace WaNut.Core.Updates;

/// <summary>
/// Updates WaNut itself from the GitHub Releases of PeanutGT/WaNut (decision 8), through Velopack. Velopack checks
/// each downloaded package against the SHA-256 in the release feed before it is applied.
/// </summary>
public sealed class AppUpdater
{
    /// <summary>
    /// A local folder or https URL with a Velopack release feed to use instead of GitHub, so updates can be tested
    /// against a release that was never published.
    /// </summary>
    public const string SourceVariable = "WANUT_UPDATE_SOURCE";

    public static readonly Uri Repository = new("https://github.com/PeanutGT/WaNut");

    private readonly UpdateManager _manager;

    public AppUpdater()
    {
        var source = Environment.GetEnvironmentVariable(SourceVariable);

        // Velopack downloads with its own HttpClient (the one exception to WaNut's shared client), since it has to
        // keep working while WaNut itself is being replaced.
        var downloader = new HttpClientFileDownloader();
        IUpdateSource feed = string.IsNullOrWhiteSpace(source)
            ? new GithubSource(Repository.AbsoluteUri, accessToken: null, prerelease: false, downloader)
            : Directory.Exists(source)
                ? new SimpleFileSource(new DirectoryInfo(source))
                : new SimpleWebSource(RequireHttps(source), downloader);
        _manager = new UpdateManager(feed);
    }

    /// <summary>False when WaNut runs from a build folder instead of its installer, so there is nothing to update.</summary>
    public bool IsInstalled => _manager.IsInstalled;

    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    /// <summary>The newer release, or null when WaNut is up to date or not installed.</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        if (!IsInstalled)
        {
            return null;
        }

        return await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
    }

    public Task DownloadAsync(UpdateInfo update, IProgress<int>? progress, CancellationToken cancellationToken = default) =>
        _manager.DownloadUpdatesAsync(update, percent => progress?.Report(percent), cancellationToken);

    /// <summary>Closes WaNut, installs the downloaded update and starts the new version. Stop the server first.</summary>
    public void ApplyAndRestart(UpdateInfo update) => _manager.ApplyUpdatesAndRestart(update.TargetFullRelease);

    private static Uri RequireHttps(string source)
    {
        var uri = new Uri(source, UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException($"{SourceVariable} must be a folder or an https URL.", nameof(source));
        }

        return uri;
    }
}
