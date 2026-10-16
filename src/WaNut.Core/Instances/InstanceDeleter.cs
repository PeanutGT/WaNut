using System.Globalization;
using WaNut.Core.Settings;
using WaNut.Core.Worlds;

namespace WaNut.Core.Instances;

/// <summary>
/// Deletes a stopped server's folder, optionally keeping its world as a single-player zip first. Only a folder
/// directly inside the servers folder is ever deleted, and links inside it are removed without touching their targets.
/// </summary>
public sealed class InstanceDeleter(LauncherPaths paths, InstanceStore store, WorldExporter exporter, TimeProvider clock)
{
    private const string DeletingPrefix = ".wanut-deleting-";

    /// <summary>True when the server has a world worth keeping (a server that never started has none).</summary>
    public static bool HasWorld(string instanceDir) =>
        File.Exists(Path.Combine(instanceDir, GameSettingsStore.LevelName(instanceDir), LevelData.FileName));

    /// <summary>
    /// Deletes the server. With <paramref name="keepWorld"/> the world is first exported to a zip in
    /// <see cref="LauncherPaths.DeletedServersDir"/>; if that fails, nothing is deleted. Returns the zip's path, or null.
    /// </summary>
    public async Task<string?> DeleteAsync(
        InstanceMetadata server, bool keepWorld, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var dir = SafeInstanceDir(server.Id);
        string? zip = null;
        if (keepWorld && HasWorld(dir))
        {
            Directory.CreateDirectory(paths.DeletedServersDir);
            zip = UniqueZipPath(server.Name);
            await exporter.ExportAsync(dir, new WorldExportRequest(null, ExportTarget.Zip, zip, server.Name, null), progress, cancellationToken)
                .ConfigureAwait(false);
        }

        // Renamed first: a file still in use stops it here, with nothing deleted. After the rename the server has
        // left the list, so a delete that fails half-way can't leave a broken server behind.
        var deleting = Path.Combine(paths.InstancesDir, DeletingPrefix + server.Id);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(deleting))
            {
                DeleteFolder(deleting);
            }

            Directory.Move(dir, deleting);
        }
        catch (Exception) when (zip is not null)
        {
            // The server stays, so a second try must not leave two copies of its world behind.
            File.Delete(zip);
            throw;
        }

        DeleteLeftovers();
        progress?.Report(1);
        return zip;
    }

    /// <summary>The server's folder, checked to be a plain folder name directly inside the servers folder.</summary>
    private string SafeInstanceDir(string id)
    {
        var root = Path.GetFullPath(paths.InstancesDir);
        var dir = Path.GetFullPath(store.InstanceDir(id));
        if (id.Length == 0 || id.StartsWith('.') || id.IndexOfAny(['/', '\\', ':']) >= 0
            || !string.Equals(Path.GetDirectoryName(dir), root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to delete '{dir}': it is not a server folder inside '{root}'.");
        }

        if (!File.Exists(Path.Combine(dir, InstanceMetadata.FileName)))
        {
            throw new DirectoryNotFoundException($"'{dir}' is not a WaNut server folder.");
        }

        return dir;
    }

    /// <summary>Deletes every folder a delete renamed, including what an earlier delete could not finish.</summary>
    private void DeleteLeftovers()
    {
        foreach (var dir in Directory.EnumerateDirectories(paths.InstancesDir, DeletingPrefix + "*"))
        {
            try
            {
                DeleteFolder(dir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The server is already out of the list; whatever is still in use goes with the next delete.
            }
        }
    }

    private static void DeleteFolder(string dir)
    {
        if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            // A linked folder: remove the link only.
            Directory.Delete(dir);
            return;
        }

        // Read-only files can't be deleted; links are skipped so their targets are never touched.
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*", options))
        {
            if (file.IsReadOnly)
            {
                file.IsReadOnly = false;
            }
        }

        // Directory.Delete removes links inside the folder without following them.
        Directory.Delete(dir, recursive: true);
    }

    private string UniqueZipPath(string name)
    {
        var stem = $"{WorldFiles.SafeName(name)} {clock.GetLocalNow().ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture)}";
        var path = Path.Combine(paths.DeletedServersDir, stem + ".zip");
        for (var n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(paths.DeletedServersDir, $"{stem} ({n}).zip");
        }

        return path;
    }
}
