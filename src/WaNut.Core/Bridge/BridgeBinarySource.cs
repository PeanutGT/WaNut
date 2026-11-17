using System.Security.Cryptography;

namespace WaNut.Core.Bridge;

/// <summary>
/// Copies the bundled build from the app's bridge\&lt;rid&gt;\ folder to %APPDATA%\WaNut\Bridge\bin\&lt;first hash digits&gt;\, so
/// the copy that runs never sits in the app folder (an app update would find it locked) and a new build never
/// overwrites a running one.
/// </summary>
public sealed class BridgeBinarySource(LauncherPaths paths, string? bundledDir = null) : IBridgeBinarySource
{
    private readonly string _bundledDir = bundledDir ?? Path.Combine(AppContext.BaseDirectory, "bridge");

    public string CacheDir => Path.Combine(paths.Root, "Bridge", "bin");

    public async Task<BridgeBinary> GetAsync(string rid, CancellationToken cancellationToken = default)
    {
        var name = BridgeBinaries.FileName(rid);
        var bundled = Path.Combine(_bundledDir, rid, name);
        if (!File.Exists(bundled))
        {
            throw new BridgeDeployException(BridgeDeployStep.CopyFiles, BridgeDeployError.BinaryUnavailable, rid);
        }

        var hash = await HashAsync(bundled, cancellationToken).ConfigureAwait(false);
        var target = Path.Combine(CacheDir, hash[..12], name);
        if (!File.Exists(target) || await HashAsync(target, cancellationToken).ConfigureAwait(false) != hash)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(bundled, target + ".part", overwrite: true);
            File.Move(target + ".part", target, overwrite: true);
        }

        return new BridgeBinary(rid, target, hash);
    }

    /// <summary>Removes cached builds no file in <paramref name="keep"/> is in; one still running stays, locked, until next time.</summary>
    public void DeleteOthers(IReadOnlyCollection<string> keep)
    {
        if (!Directory.Exists(CacheDir))
        {
            return;
        }

        var kept = keep.Select(p => Path.GetDirectoryName(Path.GetFullPath(p))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.EnumerateDirectories(CacheDir).Where(d => !kept.Contains(Path.GetFullPath(d))))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use by another server's bridge.
            }
        }
    }

    public static async Task<string> HashAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }
}
