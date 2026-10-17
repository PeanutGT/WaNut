using System.IO.Compression;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Mods;
using WaNut.Core.Properties;
using WaNut.Core.Settings;

namespace WaNut.Core.Modpacks;

/// <summary>
/// Puts a Modrinth pack into a freshly created server: downloads every server file (each checked against the
/// pack's hash), then copies "overrides/" and after it "server-overrides/", so the server's own files win.
/// A CurseForge server pack already holds its files, so those are copied, without the author's start files and
/// loader install, which WaNut sets up itself.
/// </summary>
public sealed class ModpackInstaller(FileDownloader downloader)
{
    private const string OverridesFolder = "overrides/";
    private const string ServerOverridesFolder = "server-overrides/";

    // Files WaNut writes itself: a pack must not replace the server's settings record or its EULA answer.
    private static readonly string[] ProtectedFiles = [InstanceMetadata.FileName, ModLibrary.RecordsFileName, EulaFile.FileName];

    // Top-level folders of a server pack that the loader's installer (run by WaNut) or the server itself makes.
    private static readonly string[] ServerPackSkippedFolders = ["libraries", "versions", ".fabric", "logs", "crash-reports", "java", "jre", "jdk", "runtime"];

    // Top-level files of a server pack that belong to the author's own way of starting it, or that would hand out
    // operator rights or change who may join.
    private static readonly string[] ServerPackSkippedFiles =
    [
        "user_jvm_args.txt", "variables.txt", "server-setup-config.yaml", ServerPackReader.ManifestFileName, "modlist.html",
        "install.properties", "fabric-server-launcher.properties", "ops.json", "whitelist.json", "banned-players.json",
        "banned-ips.json", "usercache.json",
    ];

    private static readonly string[] ServerPackSkippedExtensions = [".jar", ".bat", ".cmd", ".sh", ".ps1", ".command", ".exe"];

    // server.properties keys a pack may not change: the whitelist (decision 21), account checks, ports and remote access.
    private static readonly string[] KeptPropertyPrefixes =
        ["white-list", "enforce-whitelist", "online-mode", "server-port", "server-ip", "enable-query", "query.", "enable-rcon", "rcon.", "management-server"];

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
        if (pack.IsServerPack)
        {
            await Task.Run(() => CopyServerPack(instanceDir, mrpackPath, pack.ServerPackRoot!, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
            progress?.Report(1);
            return;
        }

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

    private static void CopyServerPack(string instanceDir, string zipPath, string root, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries
            .Select(e => (Entry: e, Relative: e.FullName.Replace('\\', '/')))
            .Where(e => e.Relative.StartsWith(root, StringComparison.Ordinal) && !e.Relative.EndsWith('/'))
            .Select(e => (e.Entry, Relative: e.Relative[root.Length..]))
            .Where(e => !IsSkippedInServerPack(e.Relative))
            .ToList();

        // Every path is checked before anything is copied.
        var targets = entries.Select(e => (e.Entry, e.Relative, Target: Target(instanceDir, e.Relative))).ToList();
        var total = Math.Max(1, targets.Sum(t => t.Entry.Length));
        long done = 0;
        foreach (var (entry, relative, target) in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (relative.Equals(GameSettingsStore.PropertiesFileName, StringComparison.OrdinalIgnoreCase))
            {
                MergeProperties(entry, target);
            }
            else
            {
                entry.ExtractToFile(target, overwrite: true);
            }

            done += entry.Length;
            progress?.Report((double)done / total);
        }
    }

    private static bool IsSkippedInServerPack(string relative)
    {
        var slash = relative.IndexOf('/');
        if (slash >= 0)
        {
            return ServerPackSkippedFolders.Contains(relative[..slash], StringComparer.OrdinalIgnoreCase);
        }

        return IsProtected(relative)
            || ServerPackSkippedFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)
            || ServerPackSkippedExtensions.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The pack's game settings go into the server's own server.properties, except the keys WaNut keeps.</summary>
    private static void MergeProperties(ZipArchiveEntry entry, string target)
    {
        string text;
        using (var reader = new StreamReader(entry.Open()))
        {
            text = reader.ReadToEnd();
        }

        var pack = PropertiesFile.Parse(text);
        var server = PropertiesFile.Load(target);
        foreach (var key in pack.Keys.Where(k => !KeptPropertyPrefixes.Any(p => k.StartsWith(p, StringComparison.OrdinalIgnoreCase))))
        {
            server.Set(key, pack.Get(key)!);
        }

        server.Save(target);
    }
}
