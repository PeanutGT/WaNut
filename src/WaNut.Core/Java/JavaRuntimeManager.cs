using System.IO.Compression;
using WaNut.Core.Downloads;

namespace WaNut.Core.Java;

/// <summary>
/// Keeps WaNut's own portable Java runtimes in runtimes/java-&lt;major&gt;/. The user's installed Java and PATH are never used.
/// </summary>
public sealed class JavaRuntimeManager(LauncherPaths paths, AdoptiumClient adoptium, FileDownloader downloader)
{
    public string RuntimeDir(int major) => Path.Combine(paths.RuntimesDir, $"java-{major}");

    /// <summary>Path to java.exe for an installed runtime, or null when it is not installed yet.</summary>
    public string? FindJavaExe(int major)
    {
        var dir = RuntimeDir(major);
        if (!Directory.Exists(dir))
        {
            return null;
        }

        // Temurin zips contain one top-level folder such as jdk-21.0.8+9-jre.
        return Directory
            .EnumerateFiles(dir, "java.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3 })
            .FirstOrDefault(p => string.Equals(Path.GetFileName(Path.GetDirectoryName(p)), "bin", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> EnsureInstalledAsync(
        int major,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var existing = FindJavaExe(major);
        if (existing is not null)
        {
            return existing;
        }

        var package = await adoptium.GetLatestJreAsync(major, cancellationToken).ConfigureAwait(false);
        var zipPath = Path.Combine(paths.CacheDir, "java", $"java-{major}.zip");
        await downloader.DownloadAsync(package.Link, zipPath, ExpectedHash.Sha256(package.Sha256), progress, cancellationToken).ConfigureAwait(false);

        // Extract next to the final folder and rename, so a half-extracted runtime is never mistaken for a working one.
        var target = RuntimeDir(major);
        var staging = target + ".extracting";
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, staging), cancellationToken).ConfigureAwait(false);
        if (Directory.Exists(target))
        {
            // Only reached when the folder exists without a java.exe, i.e. an earlier install was damaged.
            Directory.Delete(target, recursive: true);
        }

        Directory.Move(staging, target);
        File.Delete(zipPath);

        return FindJavaExe(major)
            ?? throw new InvalidDataException($"The Java {major} download did not contain bin\\java.exe.");
    }
}
