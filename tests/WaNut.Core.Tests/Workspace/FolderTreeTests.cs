using WaNut.Core.Storage;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Workspace;

namespace WaNut.Core.Tests.Workspace;

public sealed class FolderTreeTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly CountingFileSystem _fs;
    private readonly FolderTree _tree;

    public FolderTreeTests()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "server.properties"), "a=1");
        Directory.CreateDirectory(Path.Combine(_dir.Path, "world", "region"));
        File.WriteAllText(Path.Combine(_dir.Path, "world", "level.dat"), "x");
        Directory.CreateDirectory(Path.Combine(_dir.Path, "mods"));
        _fs = new CountingFileSystem(new LocalFileSystemAdapter(_dir.Path));
        _tree = new FolderTree(_fs);
    }

    public void Dispose() => _dir.Dispose();

    private string[] Paths() => [.. _tree.Rows.Select(r => r.Node.Path)];

    [Fact]
    public async Task LoadRoot_ShowsOnlyTheTopLevel_FoldersFirst()
    {
        await _tree.LoadRootAsync();

        Assert.Equal(["mods", "world", "server.properties"], Paths());
        Assert.All(_tree.Rows, r => Assert.Equal(0, r.Depth));
        Assert.Equal(1, _fs.Lists);
    }

    [Fact]
    public async Task Toggle_ListsTheFolderOnce_AndShowsItsChildrenIndented()
    {
        await _tree.LoadRootAsync();

        await _tree.ToggleAsync("world");
        await _tree.ToggleAsync("world");
        await _tree.ToggleAsync("world");

        Assert.Equal(["mods", "world", "world/region", "world/level.dat", "server.properties"], Paths());
        Assert.Equal(1, _tree.Rows.Single(r => r.Node.Path == "world/region").Depth);
        Assert.True(_tree.Rows.Single(r => r.Node.Path == "world").IsExpanded);
        Assert.Equal(2, _fs.Lists);
    }

    [Fact]
    public async Task Toggle_Twice_HidesTheChildren()
    {
        await _tree.LoadRootAsync();
        await _tree.ToggleAsync("world");

        await _tree.ToggleAsync("world");

        Assert.Equal(["mods", "world", "server.properties"], Paths());
    }

    [Fact]
    public async Task Refresh_KeepsOpenFoldersAndPicksUpNewFiles()
    {
        await _tree.LoadRootAsync();
        await _tree.ToggleAsync("world");
        File.WriteAllText(Path.Combine(_dir.Path, "world", "new.txt"), "n");

        await _tree.RefreshAsync();

        Assert.Contains("world/new.txt", Paths());
        Assert.True(_tree.Rows.Single(r => r.Node.Path == "world").IsExpanded);
    }

    [Fact]
    public async Task Refresh_FolderGone_ClosesItAndItsOpenChildren()
    {
        await _tree.LoadRootAsync();
        await _tree.ToggleAsync("world");
        await _tree.ToggleAsync("world/region");
        Directory.Delete(Path.Combine(_dir.Path, "world"), recursive: true);

        await _tree.RefreshAsync();

        Assert.Equal(["mods", "server.properties"], Paths());
    }

    private sealed class CountingFileSystem(IServerFileSystem inner) : IServerFileSystem
    {
        public int Lists { get; private set; }

        public bool IsRemote => inner.IsRemote;

        public string WorkspaceRoot => inner.WorkspaceRoot;

        public Task<IReadOnlyList<ServerFileNode>> ListDirectoryAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default)
        {
            Lists++;
            return inner.ListDirectoryAsync(relativePath, recursive, cancellationToken);
        }

        public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default) => inner.ExistsAsync(relativePath, cancellationToken);

        public Task<string> ReadAllTextAsync(string relativePath, CancellationToken cancellationToken = default) => inner.ReadAllTextAsync(relativePath, cancellationToken);

        public Task WriteAllTextAsync(string relativePath, string content, CancellationToken cancellationToken = default) => inner.WriteAllTextAsync(relativePath, content, cancellationToken);

        public Task UploadFileAsync(string localPath, string relativePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => inner.UploadFileAsync(localPath, relativePath, progress, cancellationToken);

        public Task DownloadFileAsync(string relativePath, string localPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => inner.DownloadFileAsync(relativePath, localPath, progress, cancellationToken);

        public Task DeleteBatchAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default) => inner.DeleteBatchAsync(relativePaths, cancellationToken);

        public Task<string> CreateArchiveAsync(IEnumerable<string> targetRelativePaths, string outputArchiveRelativePath, CancellationToken cancellationToken = default) => inner.CreateArchiveAsync(targetRelativePaths, outputArchiveRelativePath, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
