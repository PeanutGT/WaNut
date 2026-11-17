using System.IO.Compression;
using WaNut.Core.Remote.Sync;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Remote.Sync;

public sealed class TarSyncTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string Source => Path.Combine(_dir.Path, "pc");

    private string Target => Path.Combine(_dir.Path, "vps");

    private void Write(string path, string text, DateTime? modifiedUtc = null)
    {
        var full = Path.Combine(Source, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        if (modifiedUtc is { } time)
        {
            File.SetLastWriteTimeUtc(full, time);
        }
    }

    /// <summary>Copies through a compressed tar held in memory, the way the SSH classes send it.</summary>
    private sealed class MemoryArchiveCopy(ISyncTree source, ISyncTree target, Func<IReadOnlyList<SyncEntry>, IReadOnlyList<SyncEntry>>? alterListing = null) : IBulkCopy
    {
        public async Task CopyAsync(IReadOnlyList<SyncEntry> files, Action<long> reportDone, CancellationToken cancellationToken = default)
        {
            var memory = new MemoryStream();
            await using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
            {
                await TarPacker.PackAsync(files, source, gzip, cancellationToken);
            }

            memory.Position = 0;
            await using var unzip = new GZipStream(memory, CompressionMode.Decompress);
            await TarUnpacker.UnpackAsync(unzip, alterListing?.Invoke(files) ?? files, target, reportDone, cancellationToken);
        }
    }

    private sealed class FailingCopy : IBulkCopy
    {
        public Task CopyAsync(IReadOnlyList<SyncEntry> files, Action<long> reportDone, CancellationToken cancellationToken = default) =>
            throw new BulkTransferException("no tar here");
    }

    [Fact]
    public async Task RunAsync_WithArchiveCopy_CopiesFilesWithContentAndTimes()
    {
        var time = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        Write("server.properties", "motd=hi", time);
        var longPath = string.Join('/', Enumerable.Repeat(new string('d', 40), 6)) + "/a b & 'c'.jar";
        Write(longPath, new string('x', 100_000));
        Write("empty.txt", string.Empty);
        var source = new LocalSyncTree(Source);
        var target = new LocalSyncTree(Target);

        var result = await FolderSync.RunAsync(source, target, SyncDirection.Upload, null, default, new MemoryArchiveCopy(source, target));

        Assert.Equal(3, result.Copied);
        Assert.Equal("motd=hi", File.ReadAllText(Path.Combine(Target, "server.properties")));
        Assert.Equal(time, File.GetLastWriteTimeUtc(Path.Combine(Target, "server.properties")));
        Assert.Equal(100_000, new FileInfo(Path.Combine(Target, longPath)).Length);
        Assert.True(File.Exists(Path.Combine(Target, "empty.txt")));
    }

    [Fact]
    public async Task RunAsync_ArchiveCopyFails_CopiesFileByFileInstead()
    {
        Write("server.properties", "motd=hi");
        Write("world/level.dat", "data");
        var source = new LocalSyncTree(Source);
        var target = new LocalSyncTree(Target);

        await FolderSync.RunAsync(source, target, SyncDirection.Upload, null, default, new FailingCopy());

        Assert.Equal("data", File.ReadAllText(Path.Combine(Target, "world/level.dat")));
        Assert.Equal("motd=hi", File.ReadAllText(Path.Combine(Target, "server.properties")));
    }

    [Fact]
    public async Task UnpackAsync_ArchiveHoldsAFileNobodyAskedFor_Throws()
    {
        Write("a.txt", "a");
        Write("b.txt", "b");
        var source = new LocalSyncTree(Source);
        var target = new LocalSyncTree(Target);
        var all = await source.ListAsync();
        var onlyA = new MemoryArchiveCopy(source, target, files => files.Where(f => f.Path == "a.txt").ToList());

        await Assert.ThrowsAsync<BulkTransferException>(() => onlyA.CopyAsync(all, _ => { }));
        Assert.False(File.Exists(Path.Combine(Target, "b.txt")));
    }

    [Fact]
    public async Task UnpackAsync_SizeDiffersFromListing_Throws()
    {
        Write("a.txt", "abc");
        var source = new LocalSyncTree(Source);
        var target = new LocalSyncTree(Target);
        var wrong = new MemoryArchiveCopy(source, target, files => files.Select(f => f with { Size = 99 }).ToList());
        var listed = await source.ListAsync();

        await Assert.ThrowsAsync<BulkTransferException>(() => wrong.CopyAsync(listed, _ => { }));
        Assert.False(File.Exists(Path.Combine(Target, "a.txt")));
    }

    [Fact]
    public async Task PackAsync_FileChangedSinceListing_Throws()
    {
        Write("a.txt", "abc");
        var source = new LocalSyncTree(Source);
        var listed = (await source.ListAsync()).Select(f => f with { Size = 2 }).ToList();

        await Assert.ThrowsAsync<IOException>(() => TarPacker.PackAsync(listed, source, new MemoryStream(), default));
    }

    [Fact]
    public void Batches_SplitByBytesAndKeepOrder()
    {
        var files = Enumerable.Range(0, 5).Select(i => new SyncEntry($"f{i}", false, SshUploadCopy.BatchBytes / 2, DateTime.UnixEpoch)).ToList();

        var batches = SshUploadCopy.Batches(files).ToList();

        Assert.Equal([2, 2, 1], batches.Select(b => b.Count));
        Assert.Equal(files.Select(f => f.Path), batches.SelectMany(b => b).Select(f => f.Path));
    }

    [Fact]
    public void Batches_FileBiggerThanABatch_GetsItsOwn()
    {
        var files = new List<SyncEntry>
        {
            new("small", false, 10, DateTime.UnixEpoch),
            new("huge", false, SshUploadCopy.BatchBytes * 2, DateTime.UnixEpoch),
        };

        Assert.Equal(2, SshUploadCopy.Batches(files).Count());
    }

    [Fact]
    public void Parse_FindOutput_ReadsFilesFoldersAndOddNames()
    {
        var output = "d\t4096\t1700000000.5000000000\tworld\0f\t12\t1700000001.0000000000\tworld/level.dat\0f\t3\t1700000002.0000000000\ta\tb\nc.txt\0";

        var entries = RemoteListing.Parse(output);

        Assert.Equal(3, entries.Count);
        Assert.True(entries[0].IsDirectory);
        Assert.Equal(0, entries[0].Size);
        Assert.Equal(12, entries[1].Size);
        Assert.Equal(new DateTime(2023, 11, 14, 22, 13, 21, DateTimeKind.Utc), entries[1].ModifiedUtc);
        Assert.Equal("a\tb\nc.txt", entries[2].Path);
    }

    [Fact]
    public void Parse_NameWithBackslash_IsLeftOut()
    {
        Assert.Empty(RemoteListing.Parse("f\t1\t1700000000.0\ta\\b\0"));
    }

    [Fact]
    public void Commands_PutTheFolderInOneQuotedWord()
    {
        Assert.Contains("'\\''", TarCommands.ExtractCommand("wanut/servers/it's"));
        Assert.Contains(TarCommands.StageFolder, TarCommands.MoveCommand("wanut/servers/x"));
        Assert.Contains("--null", TarCommands.PackCommand("wanut/servers/x"));
    }
}
