using System.Globalization;
using WaNut.Core.Backups;
using WaNut.Core.Settings;
using WaNut.Core.Worlds.Nbt;

namespace WaNut.Core.Worlds;

/// <summary>
/// Turns a stopped server's world, or one of its backups, into a single-player world: a zip, or a folder in a
/// launcher's saves. The server's files are only read. Paper's split Nether and End go back inside the world, the
/// layout single player uses, and the chosen player's inventory can become the single-player host's.
/// </summary>
public sealed class WorldExporter(BackupStore backups, TimeProvider clock)
{
    private const string StagingPrefix = ".wanut-export-";

    public async Task<WorldExportResult> ExportAsync(
        string instanceDir, WorldExportRequest request, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var level = GameSettingsStore.LevelName(instanceDir);
        var folders = BackupStore.WorldFolders(instanceDir);
        if (request.Backup is null && !folders.Contains(level, StringComparer.OrdinalIgnoreCase))
        {
            throw new WorldException(WorldProblem.NoWorld);
        }

        // Staged on the destination's drive, so putting it in place is a quick rename.
        var stagingParent = request.Target == ExportTarget.Saves ? request.Destination : instanceDir;
        Directory.CreateDirectory(stagingParent);
        var size = request.Backup?.SizeBytes * 2 ?? folders.Sum(f => WorldFiles.FolderSize(Path.Combine(instanceDir, f)));
        WorldFiles.EnsureFreeSpace(stagingParent, request.Target == ExportTarget.Zip ? size * 2 : size);

        var staging = Path.Combine(stagingParent, StagingPrefix + clock.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        try
        {
            var copyShare = request.Target == ExportTarget.Zip ? 0.5 : 1.0;
            await StageAsync(instanceDir, request.Backup, folders, staging, Scaled(progress, 0, copyShare), cancellationToken).ConfigureAwait(false);

            var world = Path.Combine(staging, level);
            if (!File.Exists(Path.Combine(world, LevelData.FileName)))
            {
                throw new WorldException(WorldProblem.NoWorld);
            }

            WorldLayout.Join(staging, level);
            var problem = request.Player is { } uuid ? SetHost(world, uuid) : null;

            var name = WorldFiles.SafeName(request.WorldName);
            string path;
            if (request.Target == ExportTarget.Zip)
            {
                path = request.Destination;
                var temp = path + ".wanut-tmp";
                File.Delete(temp);
                var total = Math.Max(1, WorldFiles.FolderSize(world));
                long done = 0;
                var zipProgress = Scaled(progress, copyShare, 1);
                try
                {
                    await Task.Run(() => WorldFiles.ZipFolder(world, name, temp, b => zipProgress?.Report((double)(done += b) / total), cancellationToken), cancellationToken)
                        .ConfigureAwait(false);
                    File.Move(temp, path, overwrite: true);
                }
                catch
                {
                    File.Delete(temp);
                    throw;
                }
            }
            else
            {
                path = UniqueFolder(request.Destination, name);
                Directory.Move(world, path);
            }

            return new WorldExportResult(path, problem);
        }
        finally
        {
            WorldFiles.DeleteQuietly(staging);
        }
    }

    private async Task StageAsync(
        string instanceDir, BackupInfo? backup, IReadOnlyList<string> folders, string staging, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (backup is not null)
        {
            await backups.ExtractAsync(backup, staging, progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        var total = Math.Max(1, folders.Sum(f => WorldFiles.FolderSize(Path.Combine(instanceDir, f))));
        long done = 0;
        await Task.Run(
            () =>
            {
                foreach (var folder in folders)
                {
                    WorldFiles.CopyFolder(Path.Combine(instanceDir, folder), Path.Combine(staging, folder), b => progress?.Report((double)(done += b) / total), cancellationToken);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Single player reads the host from level.dat, so the player's own file is copied in there.</summary>
    private static InventoryProblem? SetHost(string world, Guid uuid)
    {
        var file = WorldLayout.FindPlayerFile(world, uuid);
        if (file is null)
        {
            return InventoryProblem.NoPlayerData;
        }

        try
        {
            LevelData.WritePlayer(Path.Combine(world, LevelData.FileName), NbtFile.Read(file).Root);
            return null;
        }
        catch (InvalidDataException)
        {
            return InventoryProblem.DamagedPlayerData;
        }
    }

    /// <summary>"My Server", then "My Server (2)", so an export never overwrites a world already in saves.</summary>
    private static string UniqueFolder(string savesDir, string name)
    {
        for (var n = 1; ; n++)
        {
            var path = Path.Combine(savesDir, n == 1 ? name : $"{name} ({n})");
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return path;
            }
        }
    }

    private static IProgress<double>? Scaled(IProgress<double>? progress, double from, double to) =>
        progress is null ? null : new Downloads.SyncProgress<double>(f => progress.Report(from + ((to - from) * f)));
}
