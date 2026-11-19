using WaNut.Core.Hotplug;
using WaNut.Core.Remote.Files;

namespace WaNut.Core.Tests.Hotplug;

public sealed class HotplugWorkspaceInitializerTests
{
    private static RemoteCommandResult Mounts(string a, string b) => new(0, a + "\0" + b, string.Empty);

    [Fact]
    public async Task InitializeAsync_SameMount_UsesFoldersNextToTheWorld()
    {
        var runner = new FakeRunner(Mounts("/home", "/home"));

        var w = await HotplugWorkspaceInitializer.InitializeAsync(runner, "/home/u/srv", "world");

        Assert.Equal(".nut-staging/datapacks", w.StagingFolder);
        Assert.Equal(".nut-backup/datapacks", w.BackupFolder);
        Assert.Equal("world/datapacks", w.DatapacksFolder);
        Assert.False(w.UsedWorldFallback);
        Assert.Equal(".nut-staging/.lock", w.LockFile);
        Assert.Equal(2, runner.Commands.Count);
    }

    [Theory]
    [InlineData("/home", "/mnt/data")]
    [InlineData("", "")]
    [InlineData("/home", "")]
    public async Task InitializeAsync_DifferentOrUnknownMount_FallsBackUnderWorld(string a, string b)
    {
        var runner = new FakeRunner(Mounts(a, b));

        var w = await HotplugWorkspaceInitializer.InitializeAsync(runner, "/home/u/srv", "My World");

        Assert.Equal("My World/.nut-staging/datapacks", w.StagingFolder);
        Assert.Equal("My World/.nut-backup/datapacks", w.BackupFolder);
        Assert.Equal("My World/.nut-staging/.lock", w.LockFile);
        Assert.True(w.UsedWorldFallback);
    }

    [Fact]
    public async Task InitializeAsync_WorldMissing_Throws()
    {
        var runner = new FakeRunner(new RemoteCommandResult(HotplugCommands.NoWorldExit, string.Empty, string.Empty));

        var e = await Assert.ThrowsAsync<HotplugWorldMissingException>(() => HotplugWorkspaceInitializer.InitializeAsync(runner, "/home/u/srv", "world"));

        Assert.Equal("world", e.WorldFolder);
    }

    [Fact]
    public async Task InitializeAsync_RootMoved_Throws()
    {
        var runner = new FakeRunner(new RemoteCommandResult(RemoteFileCommands.MovedExit, string.Empty, string.Empty));

        var e = await Assert.ThrowsAsync<WorkspaceRootException>(() => HotplugWorkspaceInitializer.InitializeAsync(runner, "/home/u/srv"));

        Assert.Equal(WorkspaceRootProblem.Moved, e.Problem);
    }

    [Fact]
    public async Task InitializeAsync_WorldOutsideFolder_Refused() =>
        await Assert.ThrowsAsync<ArgumentException>(() => HotplugWorkspaceInitializer.InitializeAsync(new FakeRunner(), "/home/u/srv", "../x"));

    [Fact]
    public async Task InitializeAsync_MakingFoldersFails_Throws()
    {
        var runner = new FakeRunner(Mounts("/a", "/a"), new RemoteCommandResult(1, string.Empty, "mkdir: Permission denied"));

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => HotplugWorkspaceInitializer.InitializeAsync(runner, "/home/u/srv"));

        Assert.Contains("Permission denied", e.Message);
    }
}
