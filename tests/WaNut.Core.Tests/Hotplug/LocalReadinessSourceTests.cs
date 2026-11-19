using WaNut.Core.Hotplug.Readiness;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Hotplug;

public sealed class LocalReadinessSourceTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task SnapshotAsync_MissingFolder_Empty() =>
        Assert.Empty(await new LocalReadinessSource(Path.Combine(_dir.Path, "nope")).SnapshotAsync());

    [Fact]
    public async Task SnapshotAsync_ListsFilesNotFolders()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "a.zip"), "abc");
        Directory.CreateDirectory(Path.Combine(_dir.Path, "sub"));

        var snapshot = await new LocalReadinessSource(_dir.Path).SnapshotAsync();

        Assert.Equal("a.zip", Assert.Single(snapshot).Name);
        Assert.Equal(3, snapshot[0].SizeBytes);
    }

    [Fact]
    public async Task CanReadExclusivelyAsync_OpenForWriting_False()
    {
        var path = Path.Combine(_dir.Path, "a.zip");
        await using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        var source = new LocalReadinessSource(_dir.Path);

        Assert.False(await source.CanReadExclusivelyAsync("a.zip"));
        await writer.DisposeAsync();
        Assert.True(await source.CanReadExclusivelyAsync("a.zip"));
    }

    [Fact]
    public async Task CanReadExclusivelyAsync_Missing_False() =>
        Assert.False(await new LocalReadinessSource(_dir.Path).CanReadExclusivelyAsync("gone.zip"));
}
