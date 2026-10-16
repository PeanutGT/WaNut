using System.IO.Compression;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Mods;

namespace WaNut.Core.Modpacks;

/// <summary>
/// Puts a Modrinth pack into a freshly created server: downloads every server file (each checked against the
/// pack's hash), then copies "overrides/" and after it "server-overrides/", so the server's own files win.
/// </summary>
public sealed class ModpackInstaller(FileDownloader downloader)
{
    private const string OverridesFolder = "overrides/";
    private const string ServerOverridesFolder = "server-overrides/";

    // Files WaNut writes itself: a pack must not replace the server's settings record or its EULA answer.
    private static readonly string[] ProtectedFiles = [InstanceMetadata.FileName, ModLibrary.RecordsFileName, EulaFile.FileName];

    /// <summary>Only call on a stopped server; nothing is undone on failure, so callers remove a new server instead.</summary>
    /// <exception cref="ModpackException">A file would land outside the server folder.</exception>
    /// <exception cref="DownloadVerificationException">A download did not match the pack's hash.</exception>
    public async Task InstallAsync(
        string instanceDir,
        string mrpackPath,
        Modpack pack,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Every path is checked before anything is downloaded.
        var targets = pack.Files.Where(f => !IsProtected(f.Path)).Select(f => (File: f, Target: Target(instanceDir, f.Path))).ToList();

        // The overrides count as one more step, so the bar does not sit at 100% while they are copied.
        var steps = targets.Count + 1;
        for (var i = 0; i < targets.Count; i++)
        {
            var step = i;
            var fileProgress = progress is null
                ? null
                : new SyncProgress<DownloadProgress>(p => progress.Report((step + (p.Fraction ?? 0)) / steps));
            await downloader.DownloadAsync(targets[i].File.Url, targets[i].Target, targets[i].File.Hash, fileProgress, cancellationToken).ConfigureAwait(false);
        }

        progress?.Report((double)targets.Count / steps);
        await Task.Run(() => CopyOverrides(instanceDir, mrpackPath, cancellationToken), cancellationToken).ConfigureAwait(false);
        progress?.Report(1);
    }

    private static string Target(string instanceDir, string relative)
    {
        if (ModpackPaths.Resolve(instanceDir, relative) is not { } target || IsProtected(relative))
        {
            throw new ModpackException(ModpackProblem.UnsafeFile, relative);
        }

        return target;
    }

    private static bool IsProtected(string relative) =>
        !relative.Contains('/') && !relative.Contains('\\') && ProtectedFiles.Contains(relative, StringComparer.OrdinalIgnoreCase);

    private static void CopyOverrides(string instanceDir, string mrpackPath, CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(mrpackPath);
        foreach (var folder in new[] { OverridesFolder, ServerOverridesFolder })
        {
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(folder, StringComparison.Ordinal) || name.EndsWith('/'))
                {
                    continue;
                }

                var relative = name[folder.Length..];
                if (IsProtected(relative))
                {
                    continue;
                }

                var target = Target(instanceDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }
    }
}
