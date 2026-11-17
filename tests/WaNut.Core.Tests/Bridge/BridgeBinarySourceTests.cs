using WaNut.Core.Bridge;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Bridge;

public sealed class BridgeBinarySourceTests
{
    [Fact]
    public async Task GetAsync_Bundled_CopiesItOutOfTheAppFolderByHash()
    {
        using var temp = new TempDir();
        var bundled = Path.Combine(temp.Path, "app", "bridge", "linux-x64");
        Directory.CreateDirectory(bundled);
        await File.WriteAllTextAsync(Path.Combine(bundled, "WaNut.Bridge"), "binary");
        var source = new BridgeBinarySource(new LauncherPaths(Path.Combine(temp.Path, "data")), Path.Combine(temp.Path, "app", "bridge"));

        var binary = await source.GetAsync(BridgeBinaries.LinuxX64);

        Assert.StartsWith(source.CacheDir, binary.Path, StringComparison.Ordinal);
        Assert.Equal("binary", await File.ReadAllTextAsync(binary.Path));
        Assert.Equal(await BridgeBinarySource.HashAsync(Path.Combine(bundled, "WaNut.Bridge")), binary.Sha256);
    }

    [Fact]
    public async Task GetAsync_NotBundled_IsBinaryUnavailable()
    {
        using var temp = new TempDir();
        var source = new BridgeBinarySource(new LauncherPaths(temp.Path), Path.Combine(temp.Path, "none"));

        var error = await Assert.ThrowsAsync<BridgeDeployException>(() => source.GetAsync(BridgeBinaries.WinX64));

        Assert.Equal(BridgeDeployError.BinaryUnavailable, error.Error);
    }

    [Fact]
    public async Task DeleteOthers_KeepsOnlyFoldersInUse()
    {
        using var temp = new TempDir();
        var source = new BridgeBinarySource(new LauncherPaths(temp.Path), temp.Path);
        var keep = Path.Combine(source.CacheDir, "aaa", "WaNut.Bridge.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(keep)!);
        Directory.CreateDirectory(Path.Combine(source.CacheDir, "bbb"));
        await File.WriteAllTextAsync(keep, "x");

        source.DeleteOthers([keep]);

        Assert.True(File.Exists(keep));
        Assert.False(Directory.Exists(Path.Combine(source.CacheDir, "bbb")));
    }
}
