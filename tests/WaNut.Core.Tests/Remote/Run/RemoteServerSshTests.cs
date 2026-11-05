using Renci.SshNet;
using WaNut.Core.Hosting;
using WaNut.Core.Remote.Run;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Remote.Run;

/// <summary>
/// Runs a fake server (a sh script that talks like Minecraft) on a real SSH server through the runner script. Needs
/// WANUT_TEST_SSH_HOST, WANUT_TEST_SSH_USER and WANUT_TEST_SSH_KEY, plus WANUT_TEST_SSH_PORT when it isn't 22. Run
/// with --filter Category=Ssh; CI skips it.
/// </summary>
[Trait("Category", "Ssh")]
public sealed class RemoteServerSshTests
{
    private const string FakeJava = """
        #!/bin/sh
        log() { echo "[12:00:00] [Server thread/INFO]: $1"; }
        log "Starting minecraft server version fake ($*)"
        log 'Done (0.123s)! For help, type "help"'
        while IFS= read -r line; do
          case "$line" in
            stop) log "Stopping server"; exit 0 ;;
            "join "*) log "${line#join } joined the game" ;;
            crash) exit 3 ;;
            hang) trap '' TERM; while :; do sleep 1; done ;;
            *) log "Unknown command: $line" ;;
          esac
        done
        """;

    [Fact]
    public async Task StartCommandsAndStop_OverSsh_FollowsTheConsole()
    {
        var root = await PrepareAsync();
        try
        {
            using var server = new RemoteServer(new ServerLaunch($"{root}/java", ["-Xmx1G", "it's quoted"], root), ConnectAsync);
            var lines = new List<string>();
            server.OutputReceived += line => { lock (lines) { lines.Add(line); } };

            server.Start();
            await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);
            await server.SendCommandAsync("join Steve");
            await FakeServer.WaitUntilAsync(() => server.Players.Contains("Steve"));
            await FakeServer.WaitUntilAsync(() => server.GetUsage() is { MemoryBytes: > 0 });

            Assert.True(await server.StopAsync(TimeSpan.FromSeconds(15)));
            Assert.Equal(0, await server.Exited);
            Assert.True(server.StopRequested);
            Assert.False(server.ConnectionLost);
            Assert.Contains(lines, l => l.Contains("(-Xmx1G it's quoted)", StringComparison.Ordinal));
            Assert.Empty(server.Players);
        }
        finally
        {
            await CleanUpAsync(root);
        }
    }

    [Fact]
    public async Task Crash_OverSsh_ReturnsTheExitCode()
    {
        var root = await PrepareAsync();
        try
        {
            using var server = new RemoteServer(new ServerLaunch($"{root}/java", [], root), ConnectAsync);
            server.Start();
            await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);
            await server.SendCommandAsync("crash");

            Assert.Equal(3, await server.Exited.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.False(server.StopRequested);
        }
        finally
        {
            await CleanUpAsync(root);
        }
    }

    [Fact]
    public async Task Kill_OverSsh_EndsAServerThatIgnoresStop()
    {
        var root = await PrepareAsync();
        try
        {
            using var server = new RemoteServer(new ServerLaunch($"{root}/java", [], root), ConnectAsync);
            server.Start();
            await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);
            await server.SendCommandAsync("hang");

            Assert.False(await server.StopAsync(TimeSpan.FromSeconds(2)));
            server.Kill();

            Assert.Equal(137, await server.Exited.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.True(server.WasKilled);
        }
        finally
        {
            await CleanUpAsync(root);
        }
    }

    [Fact]
    public async Task InstallJava_OverSsh_ChecksTheHashAndUnpacks()
    {
        var root = await PrepareAsync();
        using var ssh = await ConnectAsync(CancellationToken.None);
        var major = 900 + Random.Shared.Next(99);
        try
        {
            // A tarball shaped like Temurin's (one top folder with bin/java), served as file:// so no internet is needed.
            var sha = (await WaNut.Core.Remote.VpsConnector.RunAsync(ssh,
                $"cd \"$HOME/{root}\" && mkdir -p jdk/bin && printf '#!/bin/sh\\necho fake' > jdk/bin/java && chmod +x jdk/bin/java && tar -czf jre.tar.gz jdk && sha256sum jre.tar.gz | cut -d' ' -f1")).Trim();
            var home = (await WaNut.Core.Remote.VpsConnector.RunAsync(ssh, "printf %s \"$HOME\"")).Trim();
            var link = new Uri($"file://{home}/{root}/jre.tar.gz");

            var bad = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                WaNut.Core.Remote.VpsConnector.RunAsync(ssh, RemoteJava.InstallCommand(new WaNut.Core.Java.JavaPackage(major, "x", link, new string('0', 64), 1))));
            Assert.Contains("checksum", bad.Message, StringComparison.Ordinal);
            Assert.Equal("no", (await WaNut.Core.Remote.VpsConnector.RunAsync(ssh, RemoteJava.IsInstalledCommand(major))).Trim());

            await WaNut.Core.Remote.VpsConnector.RunAsync(ssh, RemoteJava.InstallCommand(new WaNut.Core.Java.JavaPackage(major, "x", link, sha.ToUpperInvariant(), 1)));

            Assert.Equal("yes", (await WaNut.Core.Remote.VpsConnector.RunAsync(ssh, RemoteJava.IsInstalledCommand(major))).Trim());
        }
        finally
        {
            await WaNut.Core.Remote.VpsConnector.RunAsync(ssh, $"rm -rf \"$HOME/{RemoteJava.Folder}/java-{major}\"");
            await CleanUpAsync(root);
        }
    }

    private static async Task<string> PrepareAsync()
    {
        var root = $"wanut-test/{Guid.NewGuid():N}";
        using var ssh = await ConnectAsync(CancellationToken.None);
        await WaNut.Core.Remote.VpsConnector.RunAsync(ssh, RunnerScript.InstallCommand());
        await WaNut.Core.Remote.VpsConnector.RunAsync(
            ssh, $"mkdir -p \"$HOME/{root}\" && cat > \"$HOME/{root}/java\" <<'EOF'\n{FakeJava.ReplaceLineEndings("\n")}\nEOF\nchmod +x \"$HOME/{root}/java\"");
        return root;
    }

    private static async Task CleanUpAsync(string root)
    {
        using var ssh = await ConnectAsync(CancellationToken.None);
        await WaNut.Core.Remote.VpsConnector.RunAsync(ssh, $"rm -rf \"$HOME/{root}\"");
    }

    private static async Task<SshClient> ConnectAsync(CancellationToken cancellationToken)
    {
        static string Need(string name) => Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"{name} isn't set; see this test class's summary.");

        var port = int.TryParse(Environment.GetEnvironmentVariable("WANUT_TEST_SSH_PORT"), out var p) ? p : 22;
        var client = new SshClient(Need("WANUT_TEST_SSH_HOST"), port, Need("WANUT_TEST_SSH_USER"), new PrivateKeyFile(Need("WANUT_TEST_SSH_KEY")));
        await client.ConnectAsync(cancellationToken);
        return client;
    }
}
