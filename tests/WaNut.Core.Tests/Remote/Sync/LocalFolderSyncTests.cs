using WaNut.Core.Downloads;
using WaNut.Core.Remote.Sync;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Remote.Sync;

public sealed class LocalFolderSyncTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string Source => Path.Combine(_dir.Path, "pc");

    private string Target => Path.Combine(_dir.Path, "vps");

    private static void Write(string root, string path, string text, DateTime? modifiedUtc = null)
    {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        if (modifiedUtc is { } time)
        {
            File.SetLastWriteTimeUtc(full, time);
        }
    }

    [Fact]
    public async Task RunAsync_EmptyTarget_CopiesEverythingWithTimes()
    {
        var time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Write(Source, "server.properties", "motd=hi", time);
        Write(Source, "world/region/r.0.0.mca", "region");
        Directory.CreateDirectory(Path.Combine(Source, "world/empty"));

        var result = await FolderSync.RunAsync(new LocalSyncTree(Source), new LocalSyncTree(Target), SyncDirection.Upload);

        Assert.Equal(2, result.Copied);
        Assert.Equal("motd=hi", File.ReadAllText(Path.Combine(Target, "server.properties")));
        Assert.Equal(time, File.GetLastWriteTimeUtc(Path.Combine(Target, "server.properties")));
        Assert.True(Directory.Exists(Path.Combine(Target, "world/empty")));
    }

    [Fact]
    public async Task RunAsync_SecondTime_CopiesOnlyWhatChanged()
    {
        Write(Source, "a.txt", "a");
        Write(Source, "b.txt", "b");
        await FolderSync.RunAsync(new LocalSyncTree(Source), new LocalSyncTree(Target), SyncDirection.Upload);

        Write(Source, "b.txt", "bigger b", DateTime.UtcNow.AddMinutes(1));
        var result = await FolderSync.RunAsync(new LocalSyncTree(Source), new LocalSyncTree(Target), SyncDirection.Upload);

        Assert.Equal(1, result.Copied);
        Assert.Equal("bigger b", File.ReadAllText(Path.Combine(Target, "b.txt")));
    }

    [Fact]
    public async Task RunAsync_Download_DeletesRemovedFilesButKeepsBackupsAndOldLogs()
    {
        Write(Source, "world/level.dat", "new");
        Write(Source, "logs/latest.log", "vps log");
        Write(Target, "world/level.dat", "old", DateTime.UtcNow.AddHours(-1));
        Write(Target, "world/gone.dat", "x");
        Write(Target, "backups/b.zip", "zip");
        Write(Target, "logs/old.log.gz", "old log");
        Write(Target, "instance.json", "{}");

        await FolderSync.RunAsync(new LocalSyncTree(Source), new LocalSyncTree(Target), SyncDirection.Download);

        Assert.Equal("new", File.ReadAllText(Path.Combine(Target, "world/level.dat")));
        Assert.False(File.Exists(Path.Combine(Target, "world/gone.dat")));
        Assert.True(File.Exists(Path.Combine(Target, "backups/b.zip")));
        Assert.True(File.Exists(Path.Combine(Target, "logs/old.log.gz")));
        Assert.Equal("vps log", File.ReadAllText(Path.Combine(Target, "logs/latest.log")));
        Assert.True(File.Exists(Path.Combine(Target, "instance.json")));
    }

    [Fact]
    public async Task RunAsync_ReportsProgressUpToTotal()
    {
        Write(Source, "a.bin", new string('x', 1000));
        Write(Source, "b.bin", new string('y', 500));
        var reports = new List<DownloadProgress>();

        await FolderSync.RunAsync(new LocalSyncTree(Source), new LocalSyncTree(Target), SyncDirection.Upload, new ListProgress(reports));

        Assert.All(reports, r => Assert.Equal(1500, r.TotalBytes));
        Assert.Equal(1500, reports[^1].BytesReceived);
        Assert.Equal(reports.Select(r => r.BytesReceived).Order(), reports.Select(r => r.BytesReceived));
    }

    [Fact]
    public async Task WriteAsync_PathOutsideRoot_IsRefused()
    {
        var tree = new LocalSyncTree(Target);

        await Assert.ThrowsAsync<ArgumentException>(() => tree.WriteAsync("../evil.txt", new MemoryStream(), DateTime.UtcNow));
    }

    [Fact]
    public void ServerRoot_RefusesPathLikeIds()
    {
        Assert.Equal("wanut/servers/my-server", SftpSyncTree.ServerRoot("my-server"));
        Assert.Throws<ArgumentException>(() => SftpSyncTree.ServerRoot("../x"));
        Assert.Throws<ArgumentException>(() => SftpSyncTree.ServerRoot(".."));
    }

    private sealed class ListProgress(List<DownloadProgress> reports) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => reports.Add(value);
    }
}
