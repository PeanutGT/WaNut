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
