using System.Net;
using Renci.SshNet;
using WaNut.Core.Bridge;
using WaNut.Core.Bridge.Vps;
using WaNut.Core.Instances;
using WaNut.Core.Remote;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Bridge.Vps;

/// <summary>
/// Deploys the real linux-x64 bridge to a real SSH server. Needs WANUT_TEST_SSH_HOST, WANUT_TEST_SSH_USER,
/// WANUT_TEST_SSH_KEY (plus WANUT_TEST_SSH_PORT when it isn't 22) and WANUT_TEST_BRIDGE_DIR (the artifacts/bridge folder
/// from scripts/publish-bridge.ps1). Run with --filter Category=Ssh; CI skips it.
/// </summary>
[Trait("Category", "Ssh")]
public sealed class SshVpsBridgeDeployerSshTests : IDisposable
{
    private const string Webhook = "https://discord.com/api/webhooks/1/abc";
    private const string ServerId = "bridge-test";
    private const string Properties = "#Minecraft server properties\nmotd=hi\n";

    private readonly TempDir _temp = new();
    private readonly LauncherPaths _paths;
    private readonly InstanceStore _store;

    public SshVpsBridgeDeployerSshTests()
    {
        _paths = new LauncherPaths(_temp.Path);
        _store = new InstanceStore(_paths);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task DeployStatusStopRemove_PlainAccount_RunsTheLoopWithAPrivateConfig()
    {
        var (deployer, vps) = await PrepareAsync();
        var target = DeploymentTarget.OnVps(vps.Id);
        try
        {
            var result = await deployer.DeployAsync(Request(target, new BridgeConfig { ServerId = ServerId, WebhookUrl = Webhook }));

            Assert.Equal(BridgeState.Running, result.Status.State);
            Assert.True(result.ServerRestartNeeded);
            Assert.Contains("enable-rcon=true", await File.ReadAllTextAsync(Path.Combine(_store.InstanceDir(ServerId), "server.properties")), StringComparison.Ordinal);
            var home = (await RunAsync("printf %s \"$HOME\"")).Trim();
            var basePath = result.RunMode == BridgeRunMode.Systemd ? VpsBridgeScripts.SystemBase : $"{home}/{VpsBridgeScripts.HomeBase}";
            Assert.Equal("600", (await RunAsync($"stat -c %a '{basePath}/{ServerId}/bridge.config.json'")).Trim());
            Assert.Equal(BridgeState.Running, (await deployer.GetStatusAsync(target, ServerId)).State);

            // Again: same executable, so no upload, and the old bridge is replaced.
            Assert.Equal(BridgeState.Running, (await deployer.DeployAsync(Request(target, new BridgeConfig { ServerId = ServerId, WebhookUrl = Webhook }))).Status.State);

            await deployer.StopAsync(target, ServerId);
            Assert.Equal(BridgeState.Stopped, (await deployer.GetStatusAsync(target, ServerId)).State);
        }
        finally
        {
            await deployer.RemoveAsync(target, ServerId);
        }

        Assert.Equal(BridgeState.NotInstalled, (await deployer.GetStatusAsync(target, ServerId)).State);
    }

    [Fact]
    public async Task DeployAsync_BridgeRefusesItsConfig_RollsBack()
    {
        var (deployer, vps) = await PrepareAsync();
        var target = DeploymentTarget.OnVps(vps.Id);
        try
        {
            // Embed without a bot token passes the Discord check here (nothing to check) but the bridge exits with 2.
            var config = new BridgeConfig { ServerId = ServerId, Presentation = DiscordPresentation.Embed };

            var error = await Assert.ThrowsAsync<BridgeDeployException>(() => deployer.DeployAsync(Request(target, config)));

            Assert.Equal(BridgeDeployError.BridgeExited, error.Error);
            Assert.Contains("botToken", error.Detail, StringComparison.Ordinal);
            Assert.Equal(Properties, await File.ReadAllTextAsync(Path.Combine(_store.InstanceDir(ServerId), "server.properties")));
            Assert.Equal(BridgeState.NotInstalled, (await deployer.GetStatusAsync(target, ServerId)).State);
        }
        finally
        {
            await deployer.RemoveAsync(target, ServerId);
        }
    }

    private BridgeDeployRequest Request(DeploymentTarget target, BridgeConfig config) => new(target, config, _store.InstanceDir(ServerId));

    private async Task<(SshVpsBridgeDeployer Deployer, VpsProfile Vps)> PrepareAsync()
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("WANUT_TEST_SSH_PORT"), out var p) ? p : 22;
        var vps = new VpsProfile { Host = Need("WANUT_TEST_SSH_HOST"), Port = port, User = Need("WANUT_TEST_SSH_USER"), Login = VpsLogin.KeyFile, KeyFile = Need("WANUT_TEST_SSH_KEY") };
        var connector = new VpsConnector(new SecretStore(_paths, new ReversingProtector()));
        try
        {
            using var first = await connector.ConnectAsync(vps);
        }
        catch (HostKeyException e)
        {
            vps = vps with { HostKey = e.Fingerprint };
        }

        _store.Save(new InstanceMetadata
        {
            Id = ServerId,
            Name = "Bridge test",
            Type = default,
            MinecraftVersion = "1.21.4",
            JavaMajor = 21,
            MemoryMb = 1024,
            CreatedAt = DateTimeOffset.UnixEpoch,
            VpsId = vps.Id,
        });
        Directory.CreateDirectory(_store.InstanceDir(ServerId));
        await File.WriteAllTextAsync(Path.Combine(_store.InstanceDir(ServerId), "server.properties"), Properties);
        await RunAsync($"mkdir -p \"$HOME/wanut/servers/{ServerId}/logs\" && : > \"$HOME/wanut/servers/{ServerId}/logs/latest.log\"");

        var http = new HttpClient(new OkHandler());
        var deployer = new SshVpsBridgeDeployer(
            connector,
            id => id == vps.Id ? vps : null,
            _store,
            new VpsSettingsSync(_store, connector),
            new BridgeBinarySource(_paths, Need("WANUT_TEST_BRIDGE_DIR")),
            new DiscordCheck(http, (_, _) => Task.CompletedTask));
        return (deployer, vps);
    }

    private static async Task<string> RunAsync(string command)
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("WANUT_TEST_SSH_PORT"), out var p) ? p : 22;
        using var ssh = new SshClient(Need("WANUT_TEST_SSH_HOST"), port, Need("WANUT_TEST_SSH_USER"), new PrivateKeyFile(Need("WANUT_TEST_SSH_KEY")));
        await ssh.ConnectAsync(CancellationToken.None);
        return await VpsConnector.RunAsync(ssh, command);
    }

    private static string Need(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new InvalidOperationException($"Set {name} to run the SSH tests.");

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }
}
