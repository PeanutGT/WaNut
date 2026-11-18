using System.Formats.Tar;
using System.IO.Compression;
using WaNut.Core.Remote;
using WaNut.Core.Remote.Files;
using WaNut.Core.Remote.Run;
using WaNut.Core.Storage;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Remote.Files;

/// <summary>
/// Through a real SSH server. Needs WANUT_TEST_SSH_HOST, WANUT_TEST_SSH_USER and WANUT_TEST_SSH_KEY (a private key
/// file), plus WANUT_TEST_SSH_PORT when it isn't 22. Run with --filter Category=Ssh; CI skips it.
/// </summary>
[Trait("Category", "Ssh")]
public sealed class RemoteVpsAdapterSshTests : IAsyncLifetime
{
    private readonly TempDir _dir = new();
    private readonly string _root = $"wanut-test/{Guid.NewGuid():N}";
    private VpsSession _session = null!;

    public async Task InitializeAsync()
    {
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

        // The first connection asks the user to confirm the host key; the test confirms it.
        var asked = await Assert.ThrowsAsync<HostKeyException>(() => connector.ConnectAsync(profile));
        _session = new VpsSession(connector, profile with { HostKey = asked.Fingerprint });
        await _session.RunAsync($"mkdir -p {RemoteShell.Home(_root)}");
    }

    public async Task DisposeAsync()
    {
        await _session.RunAsync($"rm -rf {RemoteShell.Home("wanut-test")}");
        await _session.DisposeAsync();
        _dir.Dispose();
    }

    [Fact]
    public async Task OpenAsync_HomeFolder_Refused()
    {
        var e = await Assert.ThrowsAsync<WorkspaceRootException>(() => RemoteVpsAdapter.OpenAsync(_session, string.Empty));

        Assert.Equal(WorkspaceRootProblem.HomeFolder, e.Problem);
    }

    [Fact]
    public async Task OpenAsync_SystemFolder_Refused()
    {
        var e = await Assert.ThrowsAsync<WorkspaceRootException>(() => RemoteVpsAdapter.OpenAsync(_session, "/etc"));

        Assert.Equal(WorkspaceRootProblem.SystemFolder, e.Problem);
    }

    [Fact]
    public async Task OpenAsync_MissingFolder_ThrowsDirectoryNotFound() =>
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => RemoteVpsAdapter.OpenAsync(_session, _root + "/nope"));

    [Fact]
    public async Task OpenAsync_LinkToHome_RefusedByRealPath()
    {
        await _session.RunAsync($"ln -s \"$HOME\" {RemoteShell.Home(_root + "/home-link")}");

        var e = await Assert.ThrowsAsync<WorkspaceRootException>(() => RemoteVpsAdapter.OpenAsync(_session, _root + "/home-link"));

        Assert.Equal(WorkspaceRootProblem.HomeFolder, e.Problem);
    }

    [Fact]
    public async Task WriteThenList_ListsLikeLocalAdapter()
    {
        await using var files = await RemoteVpsAdapter.OpenAsync(_session, _root);
        await files.WriteAllTextAsync("server.properties", "motd=héllo");
        await files.WriteAllTextAsync("world/region/r.0.0.mca", "r");
        await files.WriteAllTextAsync("logs/latest.log", "log");
        await files.WriteAllTextAsync("Banner.txt", "b");

        var all = await files.ListDirectoryAsync(string.Empty, recursive: true);
        var top = await files.ListDirectoryAsync(string.Empty);
        var world = await files.ListDirectoryAsync("world", recursive: true);

        Assert.True(files.IsRemote);
        Assert.StartsWith("/", files.WorkspaceRoot);
        Assert.Equal(["logs", "logs/latest.log", "world", "world/region", "world/region/r.0.0.mca", "Banner.txt", "server.properties"], all.Select(n => n.Path));
        Assert.Equal(["logs", "world", "Banner.txt", "server.properties"], top.Select(n => n.Path));
        Assert.Equal(["world/region", "world/region/r.0.0.mca"], world.Select(n => n.Path));
        Assert.Equal(McFileCategory.WorldData, world[1].Category);
        Assert.Equal(11, all.Single(n => n.Path == "server.properties").SizeBytes);
        Assert.Equal("motd=héllo", await files.ReadAllTextAsync("server.properties"));
        Assert.True(await files.ExistsAsync("world/region"));
        Assert.False(await files.ExistsAsync("nope"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => files.ListDirectoryAsync("nope"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => files.ReadAllTextAsync("nope.txt"));
    }

    [Fact]
    public async Task UploadThenDownload_RoundTripsWithProgress()
    {
        await using var files = await RemoteVpsAdapter.OpenAsync(_session, _root);
        var local = Path.Combine(_dir.Path, "big.bin");
        var bytes = new byte[300_000];
        Random.Shared.NextBytes(bytes);
        await File.WriteAllBytesAsync(local, bytes);
        var reported = new List<double>();
        var progress = new SyncProgress(reported.Add);

        await files.UploadFileAsync(local, "mods/big.jar", progress);
        var back = Path.Combine(_dir.Path, "back/big.jar");
        await files.DownloadFileAsync("mods/big.jar", back, progress);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(back));
        Assert.Equal(1, reported[^1]);
        Assert.False(File.Exists(back + ".wanut-part"));
    }

    [Fact]
    public async Task DeleteBatchAsync_NoServerFiles_RefusedAndKeepsFiles()
    {
        await using var files = await RemoteVpsAdapter.OpenAsync(_session, _root);
        await files.WriteAllTextAsync("notes.txt", "keep");

        var e = await Assert.ThrowsAsync<WorkspaceRootException>(() => files.DeleteBatchAsync(["notes.txt"]));

        Assert.Equal(WorkspaceRootProblem.NoServerFiles, e.Problem);
        Assert.True(await files.ExistsAsync("notes.txt"));
    }

    [Fact]
    public async Task DeleteBatchAsync_ServerFolder_DeletesAndSkipsMissing()
    {
        await using var files = await RemoteVpsAdapter.OpenAsync(_session, _root);
        await files.WriteAllTextAsync("eula.txt", "eula=true");
        await files.WriteAllTextAsync("logs/old.log.gz", "x");
        await files.WriteAllTextAsync("logs/latest.log", "x");
        await files.WriteAllTextAsync("crash-reports/a.txt", "x");

        await files.DeleteBatchAsync(["logs/old.log.gz", "crash-reports", "gone/never.txt", "-rf"]);

        Assert.False(await files.ExistsAsync("logs/old.log.gz"));
        Assert.False(await files.ExistsAsync("crash-reports"));
        Assert.True(await files.ExistsAsync("logs/latest.log"));
        await Assert.ThrowsAsync<ArgumentException>(() => files.DeleteBatchAsync(["."]));
        await Assert.ThrowsAsync<ArgumentException>(() => files.DeleteBatchAsync(["../x"]));
    }

    [Fact]
    public async Task DeleteBatchAsync_ThroughLinkOutOfFolder_RefusesWholeBatch()
    {
        var outside = _root + "-outside";
        await _session.RunAsync($"mkdir -p {RemoteShell.Home(outside)} && echo keep > {RemoteShell.Home(outside + "/precious.txt")} && ln -s \"$HOME\"/{RemoteShell.Quote(outside)} {RemoteShell.Home(_root + "/escape")}");
        await using var files = await RemoteVpsAdapter.OpenAsync(_session, _root);
        await files.WriteAllTextAsync("server.properties", "x");
        await files.WriteAllTextAsync("logs/a.log", "x");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => files.DeleteBatchAsync(["logs/a.log", "escape/precious.txt"]));
        Assert.True(await files.ExistsAsync("logs/a.log"));
        var stillThere = await _session.RunAsync($"cat {RemoteShell.Home(outside + "/precious.txt")}");
        Assert.Equal("keep\n", stillThere.Output);

        // The link itself is inside the folder: deleting it removes the link, not what it points to.
        await files.DeleteBatchAsync(["escape"]);
        Assert.False(await files.ExistsAsync("escape"));
        Assert.Equal("keep\n", (await _session.RunAsync($"cat {RemoteShell.Home(outside + "/precious.txt")}")).Output);
    }

    [Fact]
    public async Task ShellSensitiveNames_StayLiteral()
    {
        await using var files = await RemoteVpsAdapter.OpenAsync(_session, _root);
        const string name = "a $(touch pwned); `touch pwned2` 'q' \"d\" * x.txt";
        await files.WriteAllTextAsync("server.properties", "x");
        await files.WriteAllTextAsync(name, "x");

        Assert.Contains(await files.ListDirectoryAsync(string.Empty), n => n.Name == name);
        await files.DeleteBatchAsync([name]);
        var archive = await files.CreateArchiveAsync(["server.properties"], "backups/it's $(x).tar.gz");

        Assert.False(await files.ExistsAsync(name));
        Assert.True(await files.ExistsAsync(archive));
        Assert.False(await files.ExistsAsync("pwned"));
        Assert.False(await files.ExistsAsync("pwned2"));
    }

    [Fact]
    public async Task CreateArchiveAsync_PacksTargetsOnTheVps()
    {
        await using var files = await RemoteVpsAdapter.OpenAsync(_session, _root);
        await files.WriteAllTextAsync("world/level.dat", "level");
        await files.WriteAllTextAsync("world_nether/DIM-1/r.mca", "nether");
        await files.WriteAllTextAsync("server.properties", "x");

        var archive = await files.CreateArchiveAsync(["world", "world_nether"], "backups/world.tar.gz");
        var local = Path.Combine(_dir.Path, "world.tar.gz");
        await files.DownloadFileAsync(archive, local);

        Assert.Equal("backups/world.tar.gz", archive);
        Assert.Equal(["world/level.dat", "world_nether/DIM-1/r.mca"], ReadTarFiles(local));
        Assert.DoesNotContain(await files.ListDirectoryAsync(string.Empty), n => n.Name.StartsWith(".wanut-", StringComparison.Ordinal));
        await Assert.ThrowsAsync<FileNotFoundException>(() => files.CreateArchiveAsync(["missing"], "backups/x.tar.gz"));
    }

    [Fact]
    public async Task RunAsync_AfterConnectionDrops_ReconnectsOnce()
    {
        // Kills this connection's own sshd process, as a dropped network would.
        try
        {
            await _session.RunAsync("kill -9 $PPID");
        }
        catch (Renci.SshNet.Common.SshException)
        {
            // The command's own channel may go down with the connection.
        }

        var after = await _session.RunAsync("echo back");

        Assert.Equal("back\n", after.Output);
    }

    private static List<string> ReadTarFiles(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        var names = new List<string>();
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
            {
                names.Add(entry.Name);
            }
        }

        return [.. names.Order(StringComparer.Ordinal)];
    }

    private static string Need(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} isn't set; see this test class's summary.");

    /// <summary>Reports at once, unlike <see cref="Progress{T}"/>, which posts to the thread pool.</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
