using System.Net;
using WaNut.Core.Bridge;
using WaNut.Core.Bridge.Local;
using WaNut.Core.Instances;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Bridge.Local;

public sealed class LocalBridgeDeployerTests : IDisposable
{
    private const string Webhook = "https://discord.com/api/webhooks/1/abc";
    private const string Properties = "#Minecraft server properties\nmotd=hi\nenable-rcon=false\n";

    private readonly TempDir _temp = new();
    private readonly LauncherPaths _paths;
    private readonly FakeHost _host = new();
    private readonly string _serverDir;
    private BridgeStatus? _answer = new() { State = BridgeState.Running, ProcessId = 100 };

    public LocalBridgeDeployerTests()
    {
        _paths = new LauncherPaths(_temp.Path);
        _serverDir = Path.Combine(_paths.InstancesDir, "srv");
        Directory.CreateDirectory(_serverDir);
        File.WriteAllText(Path.Combine(_serverDir, "server.properties"), Properties);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task GetRecentLogAsync_ReturnsTheLastLinesOldestFirst_AndEmptyWithoutAFile()
    {
        Assert.Empty(await Deployer().GetRecentLogAsync(DeploymentTarget.Local, "srv", 2));
        var folder = Path.Combine(_paths.Root, "Bridge", "srv");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "bridge.log"), "one\ntwo\nthree\n");

        var lines = await Deployer().GetRecentLogAsync(DeploymentTarget.Local, "srv", 2);

        Assert.Equal(["two", "three"], lines);
    }

    [Fact]
    public async Task DeployAsync_Fresh_PatchesWritesEncryptedConfigAndStartsDetached()
    {
        var result = await Deployer().DeployAsync(Request());

        var properties = File.ReadAllText(Path.Combine(_serverDir, "server.properties"));
        Assert.Contains("enable-rcon=true", properties, StringComparison.Ordinal);
        Assert.Contains("motd=hi", properties, StringComparison.Ordinal);
        var json = File.ReadAllText(Path.Combine(_paths.Root, "Bridge", "srv", BridgeConfigJson.FileName));
        var config = BridgeConfigJson.Deserialize(json);
        Assert.StartsWith(BridgeSecrets.Prefix, config.RconPassword, StringComparison.Ordinal);
        Assert.DoesNotContain(Webhook, json, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("srv", "logs", "latest.log"), config.LogFile, StringComparison.Ordinal);
        Assert.Single(_host.Started);
        Assert.True(result.ServerRestartNeeded);
        Assert.Equal(BridgeRunMode.DetachedProcess, result.RunMode);
        Assert.Equal([BridgeDeployWarning.NoStartOnBoot], result.Warnings);
        Assert.Equal(BridgeState.Running, result.Status.State);
    }

    [Fact]
    public async Task DeployAsync_ScheduledTask_RegistersATaskAndNoBootWarning()
    {
        var result = await Deployer().DeployAsync(Request(BridgeRunMode.ScheduledTask));

        Assert.Equal([BridgeTaskXml.TaskName("srv")], _host.Tasks);
        Assert.Empty(_host.Started);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task DeployAsync_BridgeExitsAtOnce_RollsEverythingBack()
    {
        _answer = null;
        _host.DiesAtOnce = true;

        var error = await Assert.ThrowsAsync<BridgeDeployException>(() => Deployer().DeployAsync(Request()));

        Assert.Equal(BridgeDeployError.BridgeExited, error.Error);
        Assert.Equal(BridgeDeployStep.HealthCheck, error.Step);
        Assert.Equal(Properties, File.ReadAllText(Path.Combine(_serverDir, "server.properties")));
        Assert.False(File.Exists(Path.Combine(_paths.Root, "Bridge", "srv", BridgeConfigJson.FileName)));
        Assert.False(File.Exists(Path.Combine(_paths.Root, "Bridge", "srv", LocalBridgeRun.FileName)));
    }

    [Fact]
    public async Task DeployAsync_NoServerProperties_FailsBeforeChangingAnything()
    {
        File.Delete(Path.Combine(_serverDir, "server.properties"));

        var error = await Assert.ThrowsAsync<BridgeDeployException>(() => Deployer().DeployAsync(Request()));

        Assert.Equal(BridgeDeployError.ServerPropertiesMissing, error.Error);
        Assert.Empty(_host.Started);
    }

    [Fact]
    public async Task DeployAsync_WindowsService_IsRefused()
    {
        var error = await Assert.ThrowsAsync<BridgeDeployException>(() => Deployer().DeployAsync(Request(BridgeRunMode.WindowsService)));

        Assert.Equal(BridgeDeployError.InvalidRequest, error.Error);
    }

    [Fact]
    public async Task DeployAsync_Again_StopsTheOldBridgeFirst()
    {
        var deployer = Deployer();
        await deployer.DeployAsync(Request());

        await deployer.DeployAsync(Request());

        Assert.Equal(2, _host.Started.Count);
        Assert.Contains(_host.Started[0], _host.Killed);
    }

    [Fact]
    public async Task GetStatusAsync_Nothing_IsNotInstalled_AndAfterRemoveToo()
    {
        var deployer = Deployer();
        Assert.Equal(BridgeState.NotInstalled, (await deployer.GetStatusAsync(DeploymentTarget.Local, "srv")).State);
        await deployer.DeployAsync(Request());

        await deployer.RemoveAsync(DeploymentTarget.Local, "srv");

        Assert.Equal(BridgeState.NotInstalled, (await deployer.GetStatusAsync(DeploymentTarget.Local, "srv")).State);
    }

    private BridgeDeployRequest Request(BridgeRunMode mode = BridgeRunMode.Auto) =>
        new(new DeploymentTarget(DeploymentKind.Local, null, mode), new BridgeConfig { ServerId = "srv", WebhookUrl = Webhook }, _serverDir);

    private LocalBridgeDeployer Deployer()
    {
        var http = new HttpClient(new OkHandler());
        return new LocalBridgeDeployer(
            _paths,
            new InstanceStore(_paths),
            new FakeBinaries(Path.Combine(_temp.Path, "WaNut.Bridge.exe")),
            new DiscordCheck(http, (_, _) => Task.CompletedTask),
            _host,
            new ReversingProtector(),
            (_, _) => Task.FromResult(_answer),
            (_, _) => Task.CompletedTask);
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    private sealed class FakeBinaries(string path) : IBridgeBinarySource
    {
        public Task<BridgeBinary> GetAsync(string rid, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BridgeBinary(rid, path, "00"));

        public void DeleteOthers(IReadOnlyCollection<string> keep)
        {
        }
    }

    private sealed class FakeHost : ILocalBridgeHost
    {
        private readonly HashSet<int> _running = [];
        private int _next = 100;

        public bool DiesAtOnce { get; set; }

        public List<int> Started { get; } = [];

        public List<int> Killed { get; } = [];

        public List<string> Tasks { get; } = [];

        public int Start(string executable, IReadOnlyList<string> arguments)
        {
            var pid = _next++;
            Started.Add(pid);
            if (!DiesAtOnce)
            {
                _running.Add(pid);
            }

            return pid;
        }

        public bool IsRunning(int processId) => _running.Contains(processId);

        public void Kill(int processId)
        {
            Killed.Add(processId);
            _running.Remove(processId);
        }

        public Task RegisterTaskAsync(string taskName, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            Tasks.Add(taskName);
            return Task.CompletedTask;
        }

        public Task EndTaskAsync(string taskName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteTaskAsync(string taskName, CancellationToken cancellationToken = default)
        {
            Tasks.Remove(taskName);
            return Task.CompletedTask;
        }
    }
}
