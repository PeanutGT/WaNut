using WaNut.Core.Downloads;
using WaNut.Core.Hosting;
using WaNut.Core.Instances;
using WaNut.Core.Java;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Hosting;

public sealed class ServerLaunchPlannerTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly LauncherPaths _paths;
    private readonly InstanceStore _store;
    private readonly JavaRuntimeManager _java;
    private readonly StubHttpHandler _http = new();

    public ServerLaunchPlannerTests()
    {
        _paths = new LauncherPaths(_temp.Path);
        _paths.EnsureCreated();
        _store = new InstanceStore(_paths);
        var client = _http.CreateClient();
        _java = new JavaRuntimeManager(_paths, new AdoptiumClient(client), new FileDownloader(client));
    }

    public void Dispose() => _temp.Dispose();

    private InstanceMetadata CreateServer(bool withJar = true, bool eulaAccepted = true)
    {
        var id = _store.CreateFolder("Test");
        var dir = _store.InstanceDir(id);
        if (withJar)
        {
            File.WriteAllText(Path.Combine(dir, "server.jar"), "jar");
        }

        if (eulaAccepted)
        {
            EulaFile.WriteAccepted(dir, DateTimeOffset.UnixEpoch);
        }

        return new InstanceMetadata
        {
            Id = id,
            Name = "Test",
            Type = ServerType.Vanilla,
            MinecraftVersion = "1.21.1",
            JavaMajor = 21,
            MemoryMb = 2048,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
    }

    private string InstallFakeJava()
    {
        var bin = Path.Combine(_java.RuntimeDir(21), "jdk-21-jre", "bin");
        Directory.CreateDirectory(bin);
        var exe = Path.Combine(bin, "java.exe");
        File.WriteAllText(exe, "");
        return exe;
    }

    [Fact]
    public async Task PrepareAsync_ReadyServer_UsesBundledJavaInServerFolder()
    {
        var javaExe = InstallFakeJava();
        var server = CreateServer();

        var launch = await new ServerLaunchPlanner(_store, _java).PrepareAsync(server);

        Assert.Equal(javaExe, launch.FileName);
        Assert.Equal(_store.InstanceDir(server.Id), launch.WorkingDirectory);
        Assert.Contains("-Xmx2048M", launch.Arguments);
        Assert.Equal(["-jar", "server.jar", "nogui"], launch.Arguments.TakeLast(3));
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task PrepareAsync_JarMissing_ThrowsServerJarMissing()
    {
        InstallFakeJava();

        var error = await Assert.ThrowsAsync<ServerStartException>(
            () => new ServerLaunchPlanner(_store, _java).PrepareAsync(CreateServer(withJar: false)));

        Assert.Equal(ServerStartProblem.ServerJarMissing, error.Problem);
    }

    [Fact]
    public async Task PrepareAsync_EulaNotAccepted_ThrowsEulaNotAccepted()
    {
        InstallFakeJava();

        var error = await Assert.ThrowsAsync<ServerStartException>(
            () => new ServerLaunchPlanner(_store, _java).PrepareAsync(CreateServer(eulaAccepted: false)));

        Assert.Equal(ServerStartProblem.EulaNotAccepted, error.Problem);
    }
}
