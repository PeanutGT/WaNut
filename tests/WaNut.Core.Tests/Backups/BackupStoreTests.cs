using System.IO.Compression;
using WaNut.Core.Backups;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Backups;

public sealed class BackupStoreTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDir _temp = new();
    private readonly FakeClock _clock = new(Start);
    private readonly BackupStore _store;

    public BackupStoreTests() => _store = new BackupStore(_clock);

    public void Dispose() => _temp.Dispose();

    private string Dir => _temp.Path;

    private void WriteFile(string relative, string contents)
    {
        var path = Path.Combine(Dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private string ReadFile(string relative) => File.ReadAllText(Path.Combine(Dir, relative));

    private void WriteWorld(string marker = "v1")
    {
        WriteFile("world/level.dat", marker);
        WriteFile("world/session.lock", "lock");
        WriteFile("world/dimensions/minecraft/overworld/region/r.0.0.mca", "region-" + marker);
    }

    private async Task<BackupInfo> BackUpAsync(BackupReason reason = BackupReason.Start)
    {
        var backup = await _store.CreateAsync(Dir, reason);
        Assert.NotNull(backup);
        return backup;
    }

    [Fact]
    public async Task CreateAsync_NoWorld_ReturnsNull()
    {
        Assert.Null(await _store.CreateAsync(Dir, BackupReason.Start));
        Assert.Empty(_store.List(Dir));
    }

    [Fact]
    public async Task CreateAsync_World_ZipsItWithoutSessionLock()
    {
        WriteWorld();

        var backup = await BackUpAsync();

        Assert.Equal("2026-10-15_12-00-00_start.zip", backup.FileName);
        Assert.Equal(Path.Combine(Dir, "backups"), Path.GetDirectoryName(backup.FilePath));
        Assert.Equal(Start, backup.CreatedAt);
        using var zip = ZipFile.OpenRead(backup.FilePath);
        Assert.Equal(
            ["world/dimensions/minecraft/overworld/region/r.0.0.mca", "world/level.dat"],
            zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_OldPaperLayout_IncludesNetherAndEndFolders()
    {
        WriteWorld();
        WriteFile("world_nether/DIM-1/region/r.0.0.mca", "nether");
        WriteFile("world_the_end/DIM1/region/r.0.0.mca", "end");
        WriteFile("plugins/config.yml", "not a world");

        var backup = await BackUpAsync();

        using var zip = ZipFile.OpenRead(backup.FilePath);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("world_nether/DIM-1/region/r.0.0.mca", names);
        Assert.Contains("world_the_end/DIM1/region/r.0.0.mca", names);
        Assert.DoesNotContain(names, n => n.StartsWith("plugins/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_CustomLevelName_BacksUpThatFolder()
    {
        WriteFile("server.properties", "level-name=survival\n");
        WriteFile("survival/level.dat", "x");
        WriteFile("world/level.dat", "old world, not in use");

        var backup = await BackUpAsync();

        using var zip = ZipFile.OpenRead(backup.FilePath);
        Assert.Equal(["survival/level.dat"], zip.Entries.Select(e => e.FullName));
    }

    [Fact]
    public async Task CreateAsync_LevelNameLeavesTheFolder_Throws()
    {
        WriteFile("server.properties", "level-name=../elsewhere\n");

        var error = await Assert.ThrowsAsync<BackupException>(() => _store.CreateAsync(Dir, BackupReason.Start));

        Assert.Equal(BackupProblem.UnsafeWorldName, error.Problem);
    }

    [Fact]
    public async Task CreateAsync_SameSecondTwice_GetsDistinctNames()
    {
        WriteWorld();

        var first = await BackUpAsync();
        var second = await BackUpAsync();

        Assert.NotEqual(first.FilePath, second.FilePath);
        Assert.Equal("2026-10-15_12-00-00_start-2.zip", second.FileName);
        Assert.Equal(2, _store.List(Dir).Count);
    }

    [Fact]
    public async Task CreateAsync_ReportsProgressUpToOne()
    {
        WriteWorld();
        var progress = new ListProgress<double>();

        await _store.CreateAsync(Dir, BackupReason.Manual, progress);

        Assert.NotEmpty(progress.Reports);
        Assert.Equal(1.0, progress.Reports[^1]);
    }

    [Fact]
    public async Task List_NewestFirst_AndIgnoresOtherZips()
    {
        WriteWorld();
        await BackUpAsync(BackupReason.Start);
        _clock.Advance(TimeSpan.FromHours(1));
        await BackUpAsync(BackupReason.Manual);
        WriteFile("backups/my-own-copy.zip", "not ours");

        var backups = _store.List(Dir);

        Assert.Equal([BackupReason.Manual, BackupReason.Start], backups.Select(b => b.Reason));
        Assert.True(File.Exists(Path.Combine(Dir, "backups", "my-own-copy.zip")));
    }

    [Fact]
    public async Task Cleanup_KeepsNewestKeepCount()
    {
        WriteWorld();
        for (var i = 0; i < 7; i++)
        {
            await BackUpAsync();
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        var deleted = _store.Cleanup(Dir, new BackupRules { KeepCount = 5, MaxAgeDays = 30 });

        Assert.Equal(2, deleted.Count);
        var left = _store.List(Dir);
        Assert.Equal(5, left.Count);
        Assert.Equal(Start.AddMinutes(6), left[0].CreatedAt);
        Assert.Equal(Start.AddMinutes(2), left[^1].CreatedAt);
    }

    [Fact]
    public async Task Cleanup_DeletesOlderThanMaxAge()
    {
        WriteWorld();
        await BackUpAsync();
        _clock.Advance(TimeSpan.FromDays(40));
        await BackUpAsync();

        _store.Cleanup(Dir, new BackupRules { KeepCount = 5, MaxAgeDays = 30 });

        Assert.Equal([Start.AddDays(40)], _store.List(Dir).Select(b => b.CreatedAt));
    }

    [Fact]
    public async Task Cleanup_NewestIsOld_StillKept()
    {
        WriteWorld();
        await BackUpAsync();
        _clock.Advance(TimeSpan.FromDays(90));

        var deleted = _store.Cleanup(Dir, new BackupRules());

        Assert.Empty(deleted);
        Assert.Single(_store.List(Dir));
    }

    [Theory]
    [InlineData(BackupReason.Restore)]
    [InlineData(BackupReason.Change)]
    [InlineData(BackupReason.Import)]
    public async Task Cleanup_SafetyCopies_DoNotCountTowardKeepCount(BackupReason reason)
    {
        WriteWorld();
        await BackUpAsync(reason);
        for (var i = 0; i < 3; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            await BackUpAsync(BackupReason.Start);
        }

        _store.Cleanup(Dir, new BackupRules { KeepCount = 2, MaxAgeDays = 30 });

        Assert.Equal(
            [BackupReason.Start, BackupReason.Start, reason],
            _store.List(Dir).Select(b => b.Reason));
    }

    [Fact]
    public async Task Cleanup_OldRestoreSafetyCopy_ExpiresByAge()
    {
        WriteWorld();
        await BackUpAsync(BackupReason.Restore);
        _clock.Advance(TimeSpan.FromDays(40));
        await BackUpAsync(BackupReason.Start);

        _store.Cleanup(Dir, new BackupRules());

        Assert.Equal([BackupReason.Start], _store.List(Dir).Select(b => b.Reason));
    }

    [Fact]
    public async Task Cleanup_LeavesOtherZipsAlone()
    {
        WriteWorld();
        await BackUpAsync();
        WriteFile("backups/my-own-copy.zip", "not ours");
        _clock.Advance(TimeSpan.FromDays(90));
        await BackUpAsync();

        _store.Cleanup(Dir, new BackupRules { KeepCount = 1, MaxAgeDays = 1 });

        Assert.True(File.Exists(Path.Combine(Dir, "backups", "my-own-copy.zip")));
        Assert.Single(_store.List(Dir));
    }

    [Fact]
    public async Task RestoreAsync_PutsOldWorldBack_AndBacksUpCurrentFirst()
    {
        WriteWorld("v1");
        var v1 = await BackUpAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));
        File.Delete(Path.Combine(Dir, "world", "dimensions", "minecraft", "overworld", "region", "r.0.0.mca"));
        WriteFile("world/level.dat", "v2");
        WriteFile("world/new-in-v2.dat", "should disappear");

        var safety = await _store.RestoreAsync(Dir, v1);

        Assert.Equal("v1", ReadFile("world/level.dat"));
        Assert.Equal("region-v1", ReadFile("world/dimensions/minecraft/overworld/region/r.0.0.mca"));
        Assert.False(File.Exists(Path.Combine(Dir, "world", "new-in-v2.dat")));
        Assert.NotNull(safety);
        Assert.Equal(BackupReason.Restore, safety.Reason);
        using (var zip = ZipFile.OpenRead(safety.FilePath))
        {
            Assert.Contains(zip.Entries, e => e.FullName == "world/new-in-v2.dat");
        }

        Assert.DoesNotContain(Directory.EnumerateDirectories(Dir), d => Path.GetFileName(d).StartsWith(".wanut-restore-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoreAsync_NoCurrentWorld_RestoresWithoutSafetyBackup()
    {
        WriteWorld("v1");
        var v1 = await BackUpAsync();
        Directory.Delete(Path.Combine(Dir, "world"), recursive: true);

        var safety = await _store.RestoreAsync(Dir, v1);

        Assert.Null(safety);
        Assert.Equal("v1", ReadFile("world/level.dat"));
    }

    [Fact]
    public async Task RestoreAsync_RemovesNetherFolderTheBackupDidNotHave()
    {
        WriteWorld("v1");
        var v1 = await BackUpAsync();
        WriteFile("world_nether/DIM-1/region/r.0.0.mca", "nether");

        await _store.RestoreAsync(Dir, v1);

        Assert.False(Directory.Exists(Path.Combine(Dir, "world_nether")));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("world/../../evil.txt")]
    [InlineData("level.dat")]
    public async Task RestoreAsync_UnsafeEntry_ThrowsAndLeavesWorldAlone(string entryName)
    {
        WriteWorld("current");
        var zipPath = Path.Combine(Dir, "backups", "2026-10-01_00-00-00_manual.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry(entryName).Open());
            writer.Write("bad");
        }

        var backup = _store.List(Dir).Single();
        var error = await Assert.ThrowsAsync<BackupException>(() => _store.RestoreAsync(Dir, backup));

        Assert.Equal(BackupProblem.DamagedBackup, error.Problem);
        Assert.Equal("current", ReadFile("world/level.dat"));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(Dir)!, "evil.txt")));
        Assert.Single(_store.List(Dir));
    }

    [Fact]
    public async Task RestoreAsync_NotAZip_ThrowsDamaged()
    {
        WriteWorld("current");
        WriteFile("backups/2026-10-01_00-00-00_manual.zip", "this is not a zip");

        var error = await Assert.ThrowsAsync<BackupException>(() => _store.RestoreAsync(Dir, _store.List(Dir).Single()));

        Assert.Equal(BackupProblem.DamagedBackup, error.Problem);
        Assert.Equal("current", ReadFile("world/level.dat"));
    }

    [Fact]
    public async Task RestoreAsync_FileGone_ThrowsMissing()
    {
        WriteWorld();
        var backup = await BackUpAsync();
        File.Delete(backup.FilePath);

        var error = await Assert.ThrowsAsync<BackupException>(() => _store.RestoreAsync(Dir, backup));

        Assert.Equal(BackupProblem.BackupMissing, error.Problem);
    }

    [Fact]
    public async Task ResetWorldAsync_World_BacksUpThenDeletesIt()
    {
        WriteWorld();
        WriteFile("world_nether/DIM-1/level.dat", "nether");
        WriteFile("server.properties", "level-name=world");

        var safety = await _store.ResetWorldAsync(Dir);

        Assert.Equal(BackupReason.Reset, safety!.Reason);
        Assert.False(Directory.Exists(Path.Combine(Dir, "world")));
        Assert.False(Directory.Exists(Path.Combine(Dir, "world_nether")));
        Assert.Equal("level-name=world", ReadFile("server.properties"));
        using var archive = ZipFile.OpenRead(safety.FilePath);
        Assert.Contains(archive.Entries, e => e.FullName == "world/level.dat");
        Assert.Empty(Directory.EnumerateDirectories(Dir, ".wanut-*"));
    }

    [Fact]
    public async Task ResetWorldAsync_NoWorld_ReturnsNull()
    {
        Assert.Null(await _store.ResetWorldAsync(Dir));
    }

    [Fact]
    public async Task Cleanup_ResetCopy_DoesNotCountTowardKeepCount()
    {
        WriteWorld();
        await BackUpAsync(BackupReason.Reset);
        for (var i = 0; i < 3; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            await BackUpAsync();
        }

        _store.Cleanup(Dir, new BackupRules { KeepCount = 2, MaxAgeDays = 30 });

        Assert.Contains(_store.List(Dir), b => b.Reason == BackupReason.Reset);
    }

    [Fact]
    public async Task CreateAsync_World_RaisesCreated()
    {
        WriteWorld();
        (string Dir, BackupInfo Backup)? raised = null;
        _store.Created += (dir, backup) => raised = (dir, backup);

        var backup = await BackUpAsync();

        Assert.Equal((Dir, backup), raised);
    }

    [Fact]
    public async Task CopyToFolderAsync_Backup_CopiesAndAppliesRules()
    {
        using var copies = new TempDir();
        WriteWorld();
        for (var i = 0; i < 3; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            await _store.CopyToFolderAsync(await BackUpAsync(), copies.Path, new BackupRules { KeepCount = 2, MaxAgeDays = 30 });
        }

        var copied = Directory.GetFiles(copies.Path).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["2026-10-15_12-02-00_start.zip", "2026-10-15_12-03-00_start.zip"], copied);
    }

    [Fact]
    public async Task Parts_ListsWhatTheBackupHolds()
    {
        WriteWorld();
        WriteFile("world/DIM1/region/r.0.0.mca", "end");
        var backup = await BackUpAsync();

        Assert.Equal([WorldPart.Overworld, WorldPart.End], BackupStore.Parts(Dir, backup));
    }

    [Theory]
    [InlineData("world/DIM-1/region/r.0.0.mca", WorldPart.Nether)]
    [InlineData("world_nether/DIM-1/region/r.0.0.mca", WorldPart.Nether)]
    [InlineData("world/dimensions/minecraft/the_nether/region/r.0.0.mca", WorldPart.Nether)]
    [InlineData("world/DIM1/", WorldPart.End)]
    [InlineData("world_the_end/level.dat", WorldPart.End)]
    [InlineData("world/dimensions/minecraft/the_end/region/r.0.0.mca", WorldPart.End)]
    [InlineData("world/dimensions/minecraft/overworld/region/r.0.0.mca", WorldPart.Overworld)]
    [InlineData("world/playerdata/steve.dat", WorldPart.Overworld)]
    [InlineData("world/DIM-1x/region/r.0.0.mca", WorldPart.Overworld)]
    public void PartOf_KnowsEveryLayout(string path, WorldPart expected)
    {
        Assert.Equal(expected, BackupStore.PartOf("world", path));
    }

    [Fact]
    public async Task RestoreAsync_OnlyTheNether_KeepsTheRestAsItIsNow()
    {
        WriteWorld("v1");
        WriteFile("world/dimensions/minecraft/the_nether/region/r.0.0.mca", "nether-v1");
        WriteFile("world/dimensions/minecraft/the_end/region/r.0.0.mca", "end-v1");
        var v1 = await BackUpAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));
        WriteWorld("v2");
        WriteFile("world/dimensions/minecraft/the_nether/region/r.0.0.mca", "nether-v2");
        WriteFile("world/dimensions/minecraft/the_nether/region/r.1.0.mca", "griefed");
        WriteFile("world/dimensions/minecraft/the_end/region/r.0.0.mca", "end-v2");

        var safety = await _store.RestoreAsync(Dir, v1, [WorldPart.Nether]);

        Assert.Equal("nether-v1", ReadFile("world/dimensions/minecraft/the_nether/region/r.0.0.mca"));
        Assert.False(File.Exists(Path.Combine(Dir, "world", "dimensions", "minecraft", "the_nether", "region", "r.1.0.mca")));
        Assert.Equal("v2", ReadFile("world/level.dat"));
        Assert.Equal("region-v2", ReadFile("world/dimensions/minecraft/overworld/region/r.0.0.mca"));
        Assert.Equal("end-v2", ReadFile("world/dimensions/minecraft/the_end/region/r.0.0.mca"));
        Assert.Equal(BackupReason.Restore, safety?.Reason);
        Assert.DoesNotContain(Directory.EnumerateDirectories(Dir), d => Path.GetFileName(d).StartsWith(".wanut-restore-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoreAsync_OnlyTheOverworld_KeepsTheCurrentNetherAndEnd()
    {
        WriteWorld("v1");
        WriteFile("world/DIM-1/region/r.0.0.mca", "nether-v1");
        var v1 = await BackUpAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));
        WriteWorld("v2");
        WriteFile("world/DIM-1/region/r.0.0.mca", "nether-v2");
        WriteFile("world/DIM1/region/r.0.0.mca", "end-v2");

        await _store.RestoreAsync(Dir, v1, [WorldPart.Overworld]);

        Assert.Equal("v1", ReadFile("world/level.dat"));
        Assert.Equal("nether-v2", ReadFile("world/DIM-1/region/r.0.0.mca"));
        Assert.Equal("end-v2", ReadFile("world/DIM1/region/r.0.0.mca"));
    }

    [Fact]
    public async Task RestoreAsync_OnlyTheEnd_OldPaperLayout_ReplacesItsFolder()
    {
        WriteWorld("v1");
        WriteFile("world_nether/DIM-1/region/r.0.0.mca", "nether-v1");
        WriteFile("world_the_end/DIM1/region/r.0.0.mca", "end-v1");
        var v1 = await BackUpAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));
        WriteFile("world_nether/DIM-1/region/r.0.0.mca", "nether-v2");
        WriteFile("world_the_end/DIM1/region/r.0.0.mca", "end-v2");

        await _store.RestoreAsync(Dir, v1, [WorldPart.End]);

        Assert.Equal("end-v1", ReadFile("world_the_end/DIM1/region/r.0.0.mca"));
        Assert.Equal("nether-v2", ReadFile("world_nether/DIM-1/region/r.0.0.mca"));
    }

    [Fact]
    public async Task RestoreAsync_OldPaperBackupOnVanillaLayout_MovesNetherAndEndInsideTheWorld()
    {
        WriteWorld("v1");
        WriteFile("world_nether/DIM-1/region/r.0.0.mca", "nether-v1");
        WriteFile("world_the_end/DIM1/region/r.0.0.mca", "end-v1");
        var v1 = await BackUpAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));
        Directory.Delete(Path.Combine(Dir, "world_nether"), recursive: true);
        Directory.Delete(Path.Combine(Dir, "world_the_end"), recursive: true);
        WriteFile("world/DIM-1/region/r.0.0.mca", "nether-v2");

        Assert.True(BackupStore.MovesDimensions(Dir, v1, splitDimensions: false));
        await _store.RestoreAsync(Dir, v1, splitDimensions: false);

        Assert.Equal("nether-v1", ReadFile("world/DIM-1/region/r.0.0.mca"));
        Assert.Equal("end-v1", ReadFile("world/DIM1/region/r.0.0.mca"));
        Assert.False(Directory.Exists(Path.Combine(Dir, "world_nether", "DIM-1")));
    }

    [Fact]
    public async Task RestoreAsync_VanillaBackupOnOldPaperLayout_MovesNetherAndEndOut()
    {
        WriteWorld("v1");
        WriteFile("world/DIM-1/region/r.0.0.mca", "nether-v1");
        WriteFile("world/DIM1/region/r.0.0.mca", "end-v1");
        var v1 = await BackUpAsync();

        Assert.True(BackupStore.MovesDimensions(Dir, v1, splitDimensions: true));
        await _store.RestoreAsync(Dir, v1, splitDimensions: true);

        Assert.Equal("nether-v1", ReadFile("world_nether/DIM-1/region/r.0.0.mca"));
        Assert.Equal("end-v1", ReadFile("world_the_end/DIM1/region/r.0.0.mca"));
        Assert.False(Directory.Exists(Path.Combine(Dir, "world", "DIM-1")));
    }

    [Fact]
    public async Task RestoreAsync_OnlyTheNether_OldPaperBackupOnVanillaLayout_ReplacesTheNetherInsideTheWorld()
    {
        WriteWorld("v1");
        WriteFile("world_nether/DIM-1/region/r.0.0.mca", "nether-v1");
        var v1 = await BackUpAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));
        Directory.Delete(Path.Combine(Dir, "world_nether"), recursive: true);
        WriteWorld("v2");
        WriteFile("world/DIM-1/region/r.0.0.mca", "nether-v2");
        WriteFile("world/DIM1/region/r.0.0.mca", "end-v2");

        Assert.True(BackupStore.MovesDimensions(Dir, v1, splitDimensions: false, [WorldPart.Nether]));
        Assert.False(BackupStore.MovesDimensions(Dir, v1, splitDimensions: false, [WorldPart.End]));
        await _store.RestoreAsync(Dir, v1, [WorldPart.Nether], splitDimensions: false);

        Assert.Equal("nether-v1", ReadFile("world/DIM-1/region/r.0.0.mca"));
        Assert.Equal("v2", ReadFile("world/level.dat"));
        Assert.Equal("end-v2", ReadFile("world/DIM1/region/r.0.0.mca"));
    }

    [Fact]
    public async Task MovesDimensions_SameLayout_IsFalse()
    {
        WriteWorld("v1");
        WriteFile("world/DIM-1/region/r.0.0.mca", "nether-v1");
        var v1 = await BackUpAsync();

        Assert.False(BackupStore.MovesDimensions(Dir, v1, splitDimensions: false));
    }
}
