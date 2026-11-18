using System.Formats.Tar;
using System.IO.Compression;
using WaNut.Core.Properties;
using WaNut.Core.Storage;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Storage;

public sealed class LocalFileSystemAdapterTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly LocalFileSystemAdapter _fs;

    public LocalFileSystemAdapterTests() => _fs = new LocalFileSystemAdapter(_dir.Path);

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task WriteAllText_ThenRead_RoundTripsAndCreatesFolders()
    {
        await _fs.WriteAllTextAsync("config/a.yml", "key: ü");

        Assert.Equal("key: ü", await _fs.ReadAllTextAsync("config/a.yml"));
        Assert.False(File.Exists(Path.Combine(_dir.Path, "config", "a.yml.wanut-part")));
        Assert.DoesNotContain((byte)0xEF, File.ReadAllBytes(Path.Combine(_dir.Path, "config", "a.yml")).Take(1));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/../../x")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/x")]
    public async Task ReadAllText_PathOutsideRoot_Throws(string path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fs.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ListDirectory_Recursive_ListsFoldersFirstWithCategories()
    {
        Directory.CreateDirectory(Path.Combine(_dir.Path, "logs"));
        File.WriteAllText(Path.Combine(_dir.Path, "logs", "old.log.gz"), "x");
        File.WriteAllText(Path.Combine(_dir.Path, "server.properties"), "a=b");

        var top = await _fs.ListDirectoryAsync("");
        var all = await _fs.ListDirectoryAsync("", recursive: true);

        Assert.Equal(["logs", "server.properties"], top.Select(n => n.Path));
        Assert.True(top[0].IsDirectory);
        Assert.Equal(["logs", "logs/old.log.gz", "server.properties"], all.Select(n => n.Path));
        Assert.Equal(McFileCategory.LogOrCrash, all[1].Category);
        Assert.Equal(McFileCategory.Configuration, all[2].Category);
        Assert.Equal(3, all[2].SizeBytes);
    }

    [Fact]
    public async Task ListDirectory_MissingFolder_Throws()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _fs.ListDirectoryAsync("nope"));
    }

    [Fact]
    public async Task UploadThenDownload_CopiesFileAndReportsProgress()
    {
        var source = Path.Combine(_dir.Path, "..", Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllBytesAsync(source, new byte[200_000]);
        var progress = new ListProgress<double>();
        var back = source + ".back";
        try
        {
            await _fs.UploadFileAsync(source, "mods/m.jar", progress);
            await _fs.DownloadFileAsync("mods/m.jar", back);

            Assert.Equal(200_000, new FileInfo(Path.Combine(_dir.Path, "mods", "m.jar")).Length);
            Assert.Equal(200_000, new FileInfo(back).Length);
            Assert.Equal(1.0, progress.Reports[^1]);
        }
        finally
        {
            File.Delete(source);
            File.Delete(back);
        }
    }

    [Fact]
    public async Task DeleteBatch_RemovesFilesAndFoldersAndSkipsMissing()
    {
        Directory.CreateDirectory(Path.Combine(_dir.Path, "crash-reports", "sub"));
        File.WriteAllText(Path.Combine(_dir.Path, "crash-reports", "sub", "c.txt"), "x");
        File.WriteAllText(Path.Combine(_dir.Path, "keep.txt"), "x");
        File.SetAttributes(Path.Combine(_dir.Path, "crash-reports", "sub", "c.txt"), FileAttributes.ReadOnly);

        await _fs.DeleteBatchAsync(["crash-reports", "missing.txt"]);

        Assert.False(Directory.Exists(Path.Combine(_dir.Path, "crash-reports")));
        Assert.True(File.Exists(Path.Combine(_dir.Path, "keep.txt")));
    }

    [Fact]
    public async Task DeleteBatch_RootOrEscapingPath_DeletesNothing()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "keep.txt"), "x");

        await Assert.ThrowsAsync<ArgumentException>(() => _fs.DeleteBatchAsync(["keep.txt", "."]));
        await Assert.ThrowsAsync<ArgumentException>(() => _fs.DeleteBatchAsync(["keep.txt", "../x"]));

        Assert.True(File.Exists(Path.Combine(_dir.Path, "keep.txt")));
    }

    [Fact]
    public async Task CreateArchive_PacksFilesAndFolders()
    {
        Directory.CreateDirectory(Path.Combine(_dir.Path, "world", "region"));
        File.WriteAllText(Path.Combine(_dir.Path, "world", "region", "r.0.0.mca"), "data");
        File.WriteAllText(Path.Combine(_dir.Path, "eula.txt"), "eula=true");

        var path = await _fs.CreateArchiveAsync(["world", "eula.txt"], "backups/./w.tar.gz");

        Assert.Equal("backups/w.tar.gz", path);
        await using var gzip = new GZipStream(File.OpenRead(Path.Combine(_dir.Path, "backups", "w.tar.gz")), CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        var names = new List<string>();
        while (reader.GetNextEntry() is { } entry)
        {
            names.Add(entry.Name);
        }

        Assert.Equal(["world/region/r.0.0.mca", "eula.txt"], names);
    }

    [Fact]
    public async Task CreateArchive_MissingTarget_ThrowsAndLeavesNoArchive()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => _fs.CreateArchiveAsync(["nope"], "a.tar.gz"));

        Assert.False(File.Exists(Path.Combine(_dir.Path, "a.tar.gz")));
    }

    [Fact]
    public async Task PropertiesFile_SaveAsyncThenLoadAsync_KeepsUnknownLines()
    {
        await _fs.WriteAllTextAsync("server.properties", "#c\nmotd=hi\nmod-key=keep\n");

        var file = await PropertiesFile.LoadAsync(_fs, "server.properties");
        file.Set("motd", "yo");
        await file.SaveAsync(_fs, "server.properties");

        var again = await PropertiesFile.LoadAsync(_fs, "server.properties");
        Assert.Equal("yo", again.Get("motd"));
        Assert.Equal("keep", again.Get("mod-key"));
    }

    [Fact]
    public async Task PropertiesFile_LoadAsyncMissingFile_StartsEmpty()
    {
        var file = await PropertiesFile.LoadAsync(_fs, "server.properties");

        Assert.Empty(file.Keys);
    }
}
