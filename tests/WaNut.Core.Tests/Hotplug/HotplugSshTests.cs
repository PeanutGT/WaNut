using WaNut.Core.Hotplug;
using WaNut.Core.Remote;
using WaNut.Core.Remote.Files;
using WaNut.Core.Remote.Run;
using WaNut.Core.Tests.Remote;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Hotplug;

/// <summary>
/// The workspace and lock commands through a real SSH server. Needs WANUT_TEST_SSH_HOST, WANUT_TEST_SSH_USER and
/// WANUT_TEST_SSH_KEY (a private key file), plus WANUT_TEST_SSH_PORT when it isn't 22; or WANUT_TEST_LOCAL_SH=1 to use this machine's sh. Run with --filter Category=Ssh; CI skips it.
/// </summary>
[Trait("Category", "Ssh")]
public sealed class HotplugSshTests : IAsyncLifetime
{
    private readonly TempDir _dir = new();
    private readonly string _folder = $"wanut-test/{Guid.NewGuid():N}";
    private IRemoteCommandRunner _session = null!;
    private VpsSession? _vps;
    private string _root = null!;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("WANUT_TEST_LOCAL_SH") == "1")
        {
            // No SSH server around: run the same sh commands on this machine's sh (Linux or macOS only).
            _session = new LocalShRunner();
            _root = Path.Combine(_dir.Path, "srv");
            Directory.CreateDirectory(Path.Combine(_root, "world"));
            File.WriteAllText(Path.Combine(_root, "server.properties"), string.Empty);
            _root = (await _session.RunAsync($"cd {RemoteShell.Quote(_root)} && pwd -P")).Output.Trim();
            return;
        }

        var connector = new VpsConnector(new SecretStore(new LauncherPaths(_dir.Path), new FakeProtector()));
        var profile = new VpsProfile
        {
            Name = "test",
            Host = Need("WANUT_TEST_SSH_HOST"),
            Port = int.TryParse(Environment.GetEnvironmentVariable("WANUT_TEST_SSH_PORT"), out var p) ? p : 22,
            User = Need("WANUT_TEST_SSH_USER"),
            Login = VpsLogin.KeyFile,
            KeyFile = Need("WANUT_TEST_SSH_KEY"),
        };

        var asked = await Assert.ThrowsAsync<HostKeyException>(() => connector.ConnectAsync(profile));
        _vps = new VpsSession(connector, profile with { HostKey = asked.Fingerprint });
        _session = _vps;
        await _session.RunAsync($"mkdir -p {RemoteShell.Home(_folder)}/world && touch {RemoteShell.Home(_folder)}/server.properties");
        await using var adapter = await RemoteVpsAdapter.OpenAsync(_vps, _folder);
        _root = adapter.Root;
    }

    public async Task DisposeAsync()
    {
        if (_vps is not null)
        {
            await _vps.RunAsync($"rm -rf {RemoteShell.Home("wanut-test")}");
            await _vps.DisposeAsync();
        }

        _dir.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_MakesFolders_AndIsRepeatable()
    {
        var first = await HotplugWorkspaceInitializer.InitializeAsync(_session, _root);
        var second = await HotplugWorkspaceInitializer.InitializeAsync(_session, _root);

        Assert.Equal(first, second);
        Assert.False(first.UsedWorldFallback);
        var listing = await _session.RunAsync($"cd {RemoteShell.Quote(_root)} && ls -d .nut-staging/datapacks .nut-backup/datapacks world/datapacks");
        Assert.Equal(0, listing.ExitStatus);
    }

    [Fact]
    public async Task InitializeAsync_NoWorld_Throws()
    {
        await _session.RunAsync($"rm -rf {RemoteShell.Quote(_root)}/world");

        await Assert.ThrowsAsync<HotplugWorldMissingException>(() => HotplugWorkspaceInitializer.InitializeAsync(_session, _root));
    }

    [Fact]
    public async Task InitializeAsync_FolderGone_Throws() =>
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => HotplugWorkspaceInitializer.InitializeAsync(_session, _root + "-gone"));

    [Fact]
    public async Task Lock_SecondHolderIsRefusedUntilFirstReleases()
    {
        var workspace = await HotplugWorkspaceInitializer.InitializeAsync(_session, _root);

        var first = await HotplugLock.TryAcquireAsync(_session, _root, workspace);
        var second = await HotplugLock.TryAcquireAsync(_session, _root, workspace);

        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
        Assert.Equal(Environment.ProcessId, second.Holder!.Pid);
        Assert.NotNull(second.Holder.Since);

        await first.Lock!.DisposeAsync();
        var third = await HotplugLock.TryAcquireAsync(_session, _root, workspace);

        Assert.True(third.Acquired);
        await third.Lock!.DisposeAsync();
    }

    [Fact]
    public async Task Lock_Stale_IsTakenOver_AndOldHolderCannotRelease()
    {
        var workspace = await HotplugWorkspaceInitializer.InitializeAsync(_session, _root);
        var old = (await HotplugLock.TryAcquireAsync(_session, _root, workspace, TimeSpan.FromSeconds(1))).Lock!;
        await Task.Delay(TimeSpan.FromSeconds(2.2));

        var fresh = await HotplugLock.TryAcquireAsync(_session, _root, workspace, TimeSpan.FromSeconds(1));
        var stillMine = await old.RefreshAsync();
        await old.DisposeAsync();
        var afterOldRelease = await HotplugLock.TryAcquireAsync(_session, _root, workspace);

        Assert.True(fresh.Acquired);
        Assert.False(stillMine);
        Assert.False(afterOldRelease.Acquired);
        await fresh.Lock!.DisposeAsync();
    }

    [Fact]
    public async Task Lock_Refresh_KeepsItFromGoingStale()
    {
        var workspace = await HotplugWorkspaceInitializer.InitializeAsync(_session, _root);
        var held = (await HotplugLock.TryAcquireAsync(_session, _root, workspace, TimeSpan.FromSeconds(3))).Lock!;
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.True(await held.RefreshAsync());
        await Task.Delay(TimeSpan.FromSeconds(2));

        var other = await HotplugLock.TryAcquireAsync(_session, _root, workspace, TimeSpan.FromSeconds(3));

        Assert.False(other.Acquired);
        await held.DisposeAsync();
    }

    private static string Need(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"Set {name} to run the Ssh tests.");
}
