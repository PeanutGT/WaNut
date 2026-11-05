using WaNut.Core.Remote.Sync;

namespace WaNut.Core.Tests.Remote.Sync;

public sealed class FolderSyncPlanTests
{
    private static readonly DateTime T = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private static SyncEntry File(string path, long size = 10, int secondsLater = 0) => new(path, false, size, T.AddSeconds(secondsLater));

    private static SyncEntry Dir(string path) => new(path, true, 0, T);

    private static IReadOnlyList<string> Paths(IEnumerable<SyncEntry> entries) => entries.Select(e => e.Path).ToList();

    [Fact]
    public void Plan_SameSizeAndTime_CopiesNothing()
    {
        var plan = FolderSync.Plan([Dir("world"), File("world/level.dat")], [Dir("world"), File("world/level.dat", secondsLater: 1)], SyncDirection.Upload);

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void Plan_NewOrChangedFiles_AreCopied()
    {
        var plan = FolderSync.Plan(
            [File("a.txt", size: 11), File("b.txt", secondsLater: 5), File("c.txt")],
            [File("a.txt"), File("b.txt"), File("c.txt")],
            SyncDirection.Upload);

        Assert.Equal(["a.txt", "b.txt"], Paths(plan.CopyFiles));
        Assert.Equal(11 + 10, plan.CopyBytes);
    }

    [Fact]
    public void Plan_MissingFolder_IsCreatedAndExtraOnesDeletedDeepestFirst()
    {
        var plan = FolderSync.Plan(
            [Dir("mods"), File("mods/a.jar")],
            [Dir("old"), Dir("old/inner"), File("old/inner/x.dat"), File("stale.txt")],
            SyncDirection.Upload);

        Assert.Equal(["old/inner/x.dat", "stale.txt"], Paths(plan.DeleteFiles));
        Assert.Equal(["old/inner", "old"], Paths(plan.DeleteDirectories));
        Assert.Equal(["mods"], Paths(plan.CreateDirectories));
        Assert.Equal(["mods/a.jar"], Paths(plan.CopyFiles));
    }

    [Fact]
    public void Plan_FileBecameFolder_DeletesFileThenCreatesFolder()
    {
        var plan = FolderSync.Plan([Dir("config"), File("config/x.toml")], [File("config")], SyncDirection.Download);

        Assert.Equal(["config"], Paths(plan.DeleteFiles));
        Assert.Equal(["config"], Paths(plan.CreateDirectories));
        Assert.Equal(["config/x.toml"], Paths(plan.CopyFiles));
    }

    [Fact]
    public void Plan_Upload_SkipsLogsAndNeverDeletesThem()
    {
        var plan = FolderSync.Plan(
            [Dir("logs"), File("logs/latest.log", size: 99)],
            [Dir("logs"), File("logs/2026-11-01-1.log.gz"), Dir("crash-reports"), File("crash-reports/c.txt")],
            SyncDirection.Upload);

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void Plan_Download_BringsLogsBackButKeepsOldLocalOnes()
    {
        var plan = FolderSync.Plan(
            [Dir("logs"), File("logs/latest.log", size: 99)],
            [Dir("logs"), File("logs/latest.log"), File("logs/old.log.gz")],
            SyncDirection.Download);

        Assert.Equal(["logs/latest.log"], Paths(plan.CopyFiles));
        Assert.Empty(plan.DeleteFiles);
    }

    [Fact]
    public void Plan_WaNutFilesAndWorldLock_AreLeftAloneBothWays()
    {
        string[] ignored = ["instance.json", "backups/2026.zip", "world/session.lock", "a.jar.wanut-part", ".wanut-restore-x/level.dat", "server.jar.wanut-new"];
        var source = ignored.Select(p => File(p, size: 1)).Append(Dir("backups")).Append(Dir("world")).ToList();
        var target = ignored.Select(p => File(p, size: 2)).Append(Dir("backups")).Append(Dir("world")).ToList();

        Assert.True(FolderSync.Plan(source, target, SyncDirection.Upload).IsEmpty);
        Assert.True(FolderSync.Plan([], target.Where(e => e.Path != "world").ToList(), SyncDirection.Download).IsEmpty);
    }

    [Fact]
    public void Plan_FolderThatGoes_TakesIgnoredFilesInsideWithIt()
    {
        var plan = FolderSync.Plan([], [Dir("world"), File("world/session.lock"), File("world/level.dat")], SyncDirection.Upload);

        Assert.Equal(["world/level.dat", "world/session.lock"], Paths(plan.DeleteFiles));
        Assert.Equal(["world"], Paths(plan.DeleteDirectories));
    }

    [Fact]
    public void Plan_NamesDifferingOnlyInCase_DeleteBeforeCopy()
    {
        var plan = FolderSync.Plan([File("World.txt")], [File("world.txt")], SyncDirection.Download);

        Assert.Equal(["world.txt"], Paths(plan.DeleteFiles));
        Assert.Equal(["World.txt"], Paths(plan.CopyFiles));
    }
}
