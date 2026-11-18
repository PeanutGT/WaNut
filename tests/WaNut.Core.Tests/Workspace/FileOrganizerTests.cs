using System.Formats.Tar;
using System.IO.Compression;
using WaNut.Core.Backups;
using WaNut.Core.Storage;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Workspace;

namespace WaNut.Core.Tests.Workspace;

public sealed class FileOrganizerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDir _dir = new();
    private readonly LocalFileSystemAdapter _fs;
    private readonly FileOrganizer _organizer;

    public FileOrganizerTests()
    {
        _fs = new LocalFileSystemAdapter(_dir.Path);
        _organizer = new FileOrganizer(_fs, new FakeClock(Now));
        Write("server.properties", "level-name=world\n");
    }

    public void Dispose() => _dir.Dispose();

    private string Full(string relative) => Path.Combine(_dir.Path, relative.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string relative, string content = "x", int ageDays = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Full(relative))!);
        File.WriteAllText(Full(relative), content);
        File.SetLastWriteTimeUtc(Full(relative), Now.UtcDateTime - TimeSpan.FromDays(ageDays));
    }

    private Task<CleanupPlan> EvaluateAsync(int keep = 5, int maxAge = 30) =>
        _organizer.EvaluateAsync(new CleanupRules { Backups = new BackupRules { KeepCount = keep, MaxAgeDays = maxAge } });

    [Fact]
    public async Task Evaluate_ChangesNothing()
    {
        Write("logs/2026-10-01-1.log.gz", ageDays: 40);
        Write("mods/old.jar.disabled");

        var plan = await EvaluateAsync();

        Assert.Equal(2, plan.Items.Count);
        Assert.True(File.Exists(Full("logs/2026-10-01-1.log.gz")));
        Assert.True(File.Exists(Full("mods/old.jar.disabled")));
    }

    [Fact]
    public async Task Logs_OlderThanRetention_AreSuggested_ButNeverLatestLog()
    {
        Write("logs/2026-11-01-1.log.gz", ageDays: 8);
        Write("logs/2026-11-15-1.log.gz", ageDays: 3);
        Write("logs/latest.log", ageDays: 30);
        Write("logs/debug.log", ageDays: 30);
        Write("crash-reports/crash-2026-10-01.txt", ageDays: 20);

        var plan = await EvaluateAsync();

        Assert.Equal(
            ["crash-reports/crash-2026-10-01.txt", "logs/2026-11-01-1.log.gz"],
            plan.Items.Where(i => i.Reason == CleanupReason.OldLog).Select(i => i.Path).Order());
        Assert.All(plan.Items, i => Assert.True(i.CheckedByDefault));
    }

    [Fact]
    public async Task DisabledMods_AreSuggested_Unchecked()
    {
        Write("mods/a.jar.disabled");
        Write("mods/b.jar.old");
        Write("mods-disabled/c.jar");
        Write("mods/keep.jar");

        var plan = await EvaluateAsync();

        var mods = plan.Items.Where(i => i.Reason == CleanupReason.DisabledMod).ToList();
        Assert.Equal(["mods-disabled/c.jar", "mods/a.jar.disabled", "mods/b.jar.old"], mods.Select(i => i.Path).Order(StringComparer.Ordinal));
        Assert.All(mods, i => Assert.False(i.CheckedByDefault));
    }

    [Fact]
    public async Task Backups_BeyondKeepCount_AreSuggested_NewestKept()
    {
        Write("backups/2026-11-18_10-00-00_manual.zip");
        Write("backups/2026-11-17_10-00-00_manual.zip");
        Write("backups/2026-11-16_10-00-00_manual.zip");
        Write("backups/2026-11-15_10-00-00_restore.zip");
        Write("backups/someone-elses.zip");

        var plan = await EvaluateAsync(keep: 2);

        // Same rules as the Backups tab: safety copies don't count toward the keep count, unknown zips are never touched.
        Assert.Equal(["backups/2026-11-16_10-00-00_manual.zip"], plan.Items.Select(i => i.Path));
    }

    [Fact]
    public async Task Backups_OlderThanMaxAge_AreSuggested_ButNotTheNewest()
    {
        Write("backups/2026-09-01_10-00-00_manual.zip");
        Write("backups/2026-08-01_10-00-00_manual.zip");

        var plan = await EvaluateAsync(maxAge: 30);

        Assert.Equal(["backups/2026-08-01_10-00-00_manual.zip"], plan.Items.Select(i => i.Path));
    }

    [Fact]
    public async Task LooseWorldCopy_IsArchived_AndMarkedRisky()
    {
        Write("world_backup_2025/region/r.0.0.mca", new string('a', 500));
        Write("old_world/level.dat", "b");

        var plan = await EvaluateAsync();

        var copy = Assert.Single(plan.Items, i => i.Path == "world_backup_2025");
        Assert.Equal(CleanupAction.Archive, copy.Action);
        Assert.Equal("backups/world_backup_2025.tar.gz", copy.ArchivePath);
        Assert.Equal(500, copy.SizeBytes);
        Assert.True(copy.Risky);
        Assert.False(copy.CheckedByDefault);
        Assert.True(plan.HasRisk);
        Assert.False(Assert.Single(plan.Items, i => i.Path == "old_world").Risky);
    }

    [Fact]
    public async Task TheWorld_IsNeverSuggested_EvenWhenItsNameLooksLikeACopy()
    {
        Write("server.properties", "level-name=old_world\n");
        Write("old_world/region/r.0.0.mca");
        Write("old_world_nether/DIM-1/region/r.0.0.mca");
        Write("old_world/mods-disabled.jar.disabled");

        var plan = await EvaluateAsync();

        Assert.Empty(plan.Items);
    }

    [Fact]
    public async Task Execute_DeletesOnlySelected()
    {
        Write("logs/a.log.gz", "aaaa", ageDays: 10);
        Write("logs/b.log.gz", "bb", ageDays: 10);
        var plan = await EvaluateAsync();

        var result = await _organizer.ExecutePlanAsync(plan.Items.Where(i => i.Path == "logs/a.log.gz"));

        Assert.Equal(1, result.Deleted);
        Assert.Equal(4, result.FreedBytes);
        Assert.False(File.Exists(Full("logs/a.log.gz")));
        Assert.True(File.Exists(Full("logs/b.log.gz")));
    }

    [Fact]
    public async Task Execute_SkipsAFileThatChangedSinceTheDryRun()
    {
        Write("logs/a.log.gz", "aaaa", ageDays: 10);
        var plan = await EvaluateAsync();
        Write("logs/a.log.gz", "changed", ageDays: 0);

        var result = await _organizer.ExecutePlanAsync(plan.Items);

        Assert.Equal(0, result.Deleted);
        Assert.Single(result.Skipped);
        Assert.True(File.Exists(Full("logs/a.log.gz")));
    }

    [Fact]
    public async Task Execute_PacksLooseCopy_ThenRemovesIt()
    {
        Write("world_backup/region/r.0.0.mca", "region");
        Write("backups/world_backup.tar.gz", "already here");
        var plan = await EvaluateAsync();

        var result = await _organizer.ExecutePlanAsync(plan.Items.Where(i => i.Action == CleanupAction.Archive));

        Assert.Equal(1, result.Archived);
        Assert.Equal(0, result.FreedBytes);
        Assert.False(Directory.Exists(Full("world_backup")));
        Assert.Equal("already here", File.ReadAllText(Full("backups/world_backup.tar.gz")));
        await using var gzip = new GZipStream(File.OpenRead(Full("backups/world_backup-2.tar.gz")), CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        var names = new List<string>();
        while (tar.GetNextEntry() is { } entry)
        {
            names.Add(entry.Name.TrimEnd('/'));
        }

        Assert.Contains("world_backup/region/r.0.0.mca", names);
    }

    [Fact]
    public async Task Execute_RefusesTheWorld_AndChangesNothing()
    {
        Write("logs/a.log.gz", ageDays: 10);
        Write("world/region/r.0.0.mca");
        var plan = await EvaluateAsync();
        var worldNode = (await _fs.ListDirectoryAsync(string.Empty)).Single(n => n.Path == "world");
        var forged = new CleanupItem(worldNode, CleanupReason.LooseWorldCopy, CleanupAction.Delete, 1, true);

        await Assert.ThrowsAsync<CleanupException>(() => _organizer.ExecutePlanAsync([.. plan.Items, forged]));

        Assert.True(File.Exists(Full("world/region/r.0.0.mca")));
        Assert.True(File.Exists(Full("logs/a.log.gz")));
    }

    [Fact]
    public async Task Execute_RefusesToDeleteRegionFiles_Anywhere()
    {
        Write("stray/r.0.0.mca");
        var node = (await _fs.ListDirectoryAsync("stray")).Single();
        var forged = new CleanupItem(node, CleanupReason.OldLog, CleanupAction.Delete, 1, false);

        await Assert.ThrowsAsync<CleanupException>(() => _organizer.ExecutePlanAsync([forged]));

        Assert.True(File.Exists(Full("stray/r.0.0.mca")));
    }

    [Fact]
    public void FreedBytes_CountsDeletesOnly()
    {
        var node = new ServerFileNode("a", "a", false, 10, Now.UtcDateTime, McFileCategory.Other);
        CleanupItem[] items =
        [
            new(node, CleanupReason.OldLog, CleanupAction.Delete, 10, false),
            new(node with { Path = "b" }, CleanupReason.LooseWorldCopy, CleanupAction.Archive, 99, true),
        ];

        Assert.Equal(10, CleanupPlan.FreedBytes(items));
    }
}
