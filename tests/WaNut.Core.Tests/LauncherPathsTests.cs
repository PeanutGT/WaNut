using WaNut.Core;

namespace WaNut.Core.Tests;

public sealed class LauncherPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WaNutTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Constructor_GivenRoot_PlacesAllPathsUnderRoot()
    {
        var paths = new LauncherPaths(_root);

        Assert.Equal(_root, paths.Root);
        Assert.Equal(Path.Combine(_root, "runtimes"), paths.RuntimesDir);
        Assert.Equal(Path.Combine(_root, "cache"), paths.CacheDir);
        Assert.Equal(Path.Combine(_root, "instances"), paths.InstancesDir);
        Assert.Equal(Path.Combine(_root, "config.json"), paths.ConfigFile);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankRoot_Throws(string root)
    {
        Assert.Throws<ArgumentException>(() => new LauncherPaths(root));
    }

    [Fact]
    public void EnsureCreated_MissingFolders_CreatesThem()
    {
        var paths = new LauncherPaths(_root);

        paths.EnsureCreated();

        Assert.True(Directory.Exists(paths.RuntimesDir));
        Assert.True(Directory.Exists(paths.CacheDir));
        Assert.True(Directory.Exists(paths.InstancesDir));
    }

    [Fact]
    public void EnsureCreated_CalledTwice_DoesNotThrow()
    {
        var paths = new LauncherPaths(_root);

        paths.EnsureCreated();
        paths.EnsureCreated();

        Assert.True(Directory.Exists(paths.InstancesDir));
    }

    [Fact]
    public void CreateDefault_Always_UsesWaNutFolder()
    {
        var paths = LauncherPaths.CreateDefault();

        Assert.Equal(LauncherPaths.AppFolderName, Path.GetFileName(paths.Root));
    }

    [Fact]
    public void CreateDefault_WithOverride_UsesOverrideFolder()
    {
        var paths = LauncherPaths.CreateDefault(_root);

        Assert.Equal(_root, paths.Root);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CreateDefault_NoOverride_UsesWaNutFolder(string? dataDirOverride)
    {
        var paths = LauncherPaths.CreateDefault(dataDirOverride);

        Assert.Equal(LauncherPaths.AppFolderName, Path.GetFileName(paths.Root));
    }
}
