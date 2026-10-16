using System.Globalization;
using WaNut.Core.Backups;
using WaNut.Core.Players;
using WaNut.Core.Properties;
using WaNut.Core.Settings;
using WaNut.Core.Worlds.Nbt;

namespace WaNut.Core.Worlds;

/// <summary>
/// Copies a single-player world (a folder or a zip) into a stopped server. The source is only read, never changed.
/// The copy is staged next to the server's world first; only when it is complete is the old world backed up (as an
/// "import" backup) and swapped out, so a failure at any point leaves the server as it was.
/// </summary>
public sealed class WorldImporter(BackupStore backups, MojangProfiles profiles, TimeProvider clock)
{
    private const string StagingPrefix = ".wanut-import-";

    public async Task<WorldImportResult> ImportAsync(
        string instanceDir, WorldImportRequest request, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var level = GameSettingsStore.LevelName(instanceDir);
        if (!BackupStore.IsPlainName(level))
        {
            throw new WorldException(WorldProblem.UnsafeWorldName, level);
        }

        var source = request.Source;
        var size = await Task.Run(() => source.IsZip ? WorldFiles.ZipSize(source.Path, source.ZipRoot!) : WorldFiles.FolderSize(source.Path), cancellationToken)
            .ConfigureAwait(false);
        WorldFiles.EnsureFreeSpace(instanceDir, size);

        // The name lookup needs the network, so it happens before the long copy rather than after it.
        var (owner, problem) = request.BringInventory ? await FindOwnerAsync(instanceDir, request.OwnerName, cancellationToken).ConfigureAwait(false) : (null, null);

        var staging = Path.Combine(instanceDir, StagingPrefix + clock.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        var world = Path.Combine(staging, level);
        try
        {
            long done = 0;
            var total = Math.Max(1, size);
            void Copied(long bytes) => progress?.Report((double)(done += bytes) / total);
            await Task.Run(
                () =>
                {
                    if (source.IsZip)
                    {
                        WorldFiles.ExtractFolder(source.Path, source.ZipRoot!, world, Copied, cancellationToken);
                    }
                    else
                    {
                        WorldFiles.CopyFolder(source.Path, world, Copied, cancellationToken);
                    }
                },
                cancellationToken).ConfigureAwait(false);

            if (!File.Exists(Path.Combine(world, LevelData.FileName)))
            {
                throw new WorldException(WorldProblem.NotAWorld, source.Path);
            }

            var moved = false;
            if (owner is { } uuid)
            {
                problem = MoveHostInventory(world, uuid, request.MinecraftVersion);
                moved = problem is null;
            }

            if (WorldLayout.SplitsDimensions(request.Type, request.MinecraftVersion))
            {
                WorldLayout.Split(staging, level);
            }

            var hasPack = File.Exists(Path.Combine(world, "resources.zip"));
            var folders = Directory.EnumerateDirectories(staging).Select(Path.GetFileName).OfType<string>().ToList();
            var safety = await backups.ReplaceWorldAsync(instanceDir, staging, folders, BackupReason.Import, cancellationToken).ConfigureAwait(false);
            return new WorldImportResult(moved, problem, hasPack, safety);
        }
        finally
        {
            WorldFiles.DeleteQuietly(staging);
        }
    }

    /// <summary>
    /// The host's UUID on this server: their Mojang account's with online-mode on, otherwise the one the server
    /// derives from the name. Returns the reason instead when it can't be found.
    /// </summary>
    private async Task<(Guid? Uuid, InventoryProblem? Problem)> FindOwnerAsync(string instanceDir, string? ownerName, CancellationToken cancellationToken)
    {
        var name = ownerName?.Trim();
        if (string.IsNullOrEmpty(name) || !PlayerLists.IsValidName(name))
        {
            return (null, InventoryProblem.NameNotFound);
        }

        var onlineMode = PropertiesFile.Load(Path.Combine(instanceDir, "server.properties")).Get("online-mode")?.Trim();
        if (string.Equals(onlineMode, "false", StringComparison.OrdinalIgnoreCase))
        {
            return (PlayerIds.OfflineUuid(name), null);
        }

        try
        {
            var uuid = await profiles.FindUuidAsync(name, cancellationToken).ConfigureAwait(false);
            return uuid is null ? (null, InventoryProblem.NameNotFound) : (uuid, null);
        }
        catch (HttpRequestException)
        {
            return (null, InventoryProblem.LookupFailed);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's own timeout.
            return (null, InventoryProblem.LookupFailed);
        }
    }

    /// <summary>
    /// A dedicated server ignores level.dat's Player and reads each player's own file, so the host's data is
    /// written there. Any older file of the same player in the world is replaced: single-player play keeps it in level.dat.
    /// </summary>
    private static InventoryProblem? MoveHostInventory(string world, Guid uuid, string minecraftVersion)
    {
        NbtCompound? player;
        try
        {
            player = LevelData.ReadPlayer(Path.Combine(world, LevelData.FileName));
        }
        catch (InvalidDataException)
        {
            return InventoryProblem.DamagedPlayerData;
        }

        if (player is null)
        {
            return InventoryProblem.NoPlayerInWorld;
        }

        var dir = WorldLayout.PlayerDataDir(world, minecraftVersion);
        Directory.CreateDirectory(dir);
        NbtFile.Write(Path.Combine(dir, uuid.ToString("D") + ".dat"), string.Empty, player);
        return null;
    }
}
