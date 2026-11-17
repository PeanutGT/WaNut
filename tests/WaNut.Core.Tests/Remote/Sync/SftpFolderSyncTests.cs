using Renci.SshNet;
using WaNut.Core.Remote.Sync;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Remote.Sync;

/// <summary>
/// Round trip through a real SSH server. Needs WANUT_TEST_SSH_HOST, WANUT_TEST_SSH_USER and WANUT_TEST_SSH_KEY (a
/// private key file), plus WANUT_TEST_SSH_PORT when it isn't 22. Run with --filter Category=Ssh; CI skips it.
/// </summary>
[Trait("Category", "Ssh")]
public sealed class SftpFolderSyncTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task UploadThenDownload_RoundTripsChangesAndDeletes()
    {
        using var sftp = Connect();
        var root = $"wanut-test/{Guid.NewGuid():N}";
        var remote = new SftpSyncTree(sftp, root);
        var pc = Path.Combine(_dir.Path, "pc");
        var back = Path.Combine(_dir.Path, "back");
        var time = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        Directory.CreateDirectory(Path.Combine(pc, "world/region"));
        File.WriteAllText(Path.Combine(pc, "world/region/r.0.0.mca"), new string('r', 70_000));
        File.WriteAllText(Path.Combine(pc, "server.properties"), "motd=hi");
        File.SetLastWriteTimeUtc(Path.Combine(pc, "server.properties"), time);
        Directory.CreateDirectory(Path.Combine(pc, "mods"));

        try
        {
            var first = await FolderSync.RunAsync(new LocalSyncTree(pc), remote, SyncDirection.Upload);
            var again = await FolderSync.RunAsync(new LocalSyncTree(pc), remote, SyncDirection.Upload);

            Assert.Equal(2, first.Copied);
            Assert.Equal(0, again.Copied);
            Assert.Equal(time, sftp.GetLastWriteTimeUtc($"{root}/server.properties"));

            // The server changed a file, wrote a log and removed a region while it ran on the VPS.
            sftp.WriteAllText($"{root}/server.properties", "motd=changed on vps");
            sftp.CreateDirectory($"{root}/logs");
            sftp.WriteAllText($"{root}/logs/latest.log", "log");
            sftp.DeleteFile($"{root}/world/region/r.0.0.mca");

            await FolderSync.RunAsync(remote, new LocalSyncTree(pc), SyncDirection.Download);
            await FolderSync.RunAsync(remote, new LocalSyncTree(back), SyncDirection.Download);

            Assert.Equal("motd=changed on vps", File.ReadAllText(Path.Combine(pc, "server.properties")));
            Assert.Equal("log", File.ReadAllText(Path.Combine(pc, "logs/latest.log")));
            Assert.False(File.Exists(Path.Combine(pc, "world/region/r.0.0.mca")));
            Assert.True(Directory.Exists(Path.Combine(back, "mods")));
        }
        finally
        {
            DeleteRecursive(sftp, root);
        }
    }

    [Fact]
    public async Task ArchiveSync_RoundTripsLongNamesChangesAndDeletes()
    {
        using var sftp = Connect();
        using var ssh = ConnectSsh();
        var root = $"wanut-test/{Guid.NewGuid():N}";
        var remote = new SftpSyncTree(sftp, root, ssh);
        var local = new LocalSyncTree(Path.Combine(_dir.Path, "pc"));
        var back = new LocalSyncTree(Path.Combine(_dir.Path, "back"));
        var pc = local.Root;
        var longName = string.Join('/', Enumerable.Repeat(new string('d', 40), 6)) + "/file with spaces & 'quotes'.jar";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(pc, longName))!);
        File.WriteAllBytes(Path.Combine(pc, longName), new byte[300_000]);
        File.WriteAllText(Path.Combine(pc, "server.properties"), "motd=hi");
        File.WriteAllText(Path.Combine(pc, "empty.txt"), string.Empty);

        try
        {
            // Called directly, so a silent fall back to file by file copying can't hide a broken archive transfer.
            var files = (await local.ListAsync()).Where(e => !e.IsDirectory).ToList();
            await new SshUploadCopy(ssh, local, root).CopyAsync(files, _ => { });
            var again = await FolderSync.RunAsync(local, remote, SyncDirection.Upload, null, default, new SshUploadCopy(ssh, local, root));

            Assert.Equal(0, again.Copied);
            Assert.False(sftp.Exists($"{root}/{TarCommands.StageFolder}"));

            sftp.WriteAllText($"{root}/server.properties", "motd=changed on vps");
            var listed = (await remote.ListAsync()).Where(e => !e.IsDirectory && e.Path != longName).ToList();
            await new SshDownloadCopy(ssh, root, back).CopyAsync(listed, _ => { });
            await FolderSync.RunAsync(remote, back, SyncDirection.Download, null, default, new SshDownloadCopy(ssh, root, back));

            Assert.Equal("motd=changed on vps", File.ReadAllText(Path.Combine(back.Root, "server.properties")));
            Assert.Equal(300_000, new FileInfo(Path.Combine(back.Root, longName)).Length);
            Assert.True(File.Exists(Path.Combine(back.Root, "empty.txt")));
        }
        finally
        {
            DeleteRecursive(sftp, root);
        }
    }

    [Fact]
    public async Task SettingsSync_PullsAndPushesOnlyTheSettingsFile()
    {
        using var sftp = Connect();
        var id = Guid.NewGuid().ToString("N");
        var root = SftpSyncTree.ServerRoot(id);
        var dir = Path.Combine(_dir.Path, "pc");
        Directory.CreateDirectory(dir);
        foreach (var folder in new[] { "wanut", SftpSyncTree.ServersFolder, root })
        {
            if (!sftp.Exists(folder))
            {
                sftp.CreateDirectory(folder);
            }
        }

        sftp.WriteAllText($"{root}/server.properties", "online-mode=true\n");
        sftp.WriteAllText($"{root}/level.dat", "world");
        File.WriteAllText(Path.Combine(dir, "server.properties"), "old");

        try
        {
            await WaNut.Core.Remote.VpsSettingsSync.CopyDownAsync(sftp, id, dir);
            Assert.Equal("online-mode=true\n", File.ReadAllText(Path.Combine(dir, "server.properties")));
            Assert.False(File.Exists(Path.Combine(dir, "level.dat")));

            File.WriteAllText(Path.Combine(dir, "server.properties"), "online-mode=false\n");
            await WaNut.Core.Remote.VpsSettingsSync.CopyUpAsync(sftp, id, dir);

            Assert.Equal("online-mode=false\n", sftp.ReadAllText($"{root}/server.properties"));
            Assert.Equal("world", sftp.ReadAllText($"{root}/level.dat"));

            sftp.DeleteFile($"{root}/server.properties");
            await Assert.ThrowsAsync<VpsFolderMissingException>(() => WaNut.Core.Remote.VpsSettingsSync.CopyUpAsync(sftp, id, dir));
            Assert.False(sftp.Exists($"{root}/server.properties"));
        }
        finally
        {
            DeleteRecursive(sftp, root);
        }
    }

    private static SshClient ConnectSsh()
    {
        static string Need(string name) => Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"{name} isn't set; see this test class's summary.");

        var port = int.TryParse(Environment.GetEnvironmentVariable("WANUT_TEST_SSH_PORT"), out var p) ? p : 22;
        var client = new SshClient(Need("WANUT_TEST_SSH_HOST"), port, Need("WANUT_TEST_SSH_USER"), new PrivateKeyFile(Need("WANUT_TEST_SSH_KEY")));
        client.Connect();
        return client;
    }

    private static SftpClient Connect()
    {
        static string Need(string name) => Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"{name} isn't set; see this test class's summary.");

        var port = int.TryParse(Environment.GetEnvironmentVariable("WANUT_TEST_SSH_PORT"), out var p) ? p : 22;
        var client = new SftpClient(Need("WANUT_TEST_SSH_HOST"), port, Need("WANUT_TEST_SSH_USER"), new PrivateKeyFile(Need("WANUT_TEST_SSH_KEY")));
        client.Connect();
        return client;
    }

    private static void DeleteRecursive(SftpClient sftp, string path)
    {
        foreach (var file in sftp.ListDirectory(path).Where(f => f.Name is not "." and not ".."))
        {
            if (file.IsDirectory)
            {
                DeleteRecursive(sftp, file.FullName);
            }
            else
            {
                sftp.DeleteFile(file.FullName);
            }
        }

        sftp.DeleteDirectory(path);
    }
}
