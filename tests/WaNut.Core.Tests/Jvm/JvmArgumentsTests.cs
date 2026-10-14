using WaNut.Core.Jvm;

namespace WaNut.Core.Tests.Jvm;

public sealed class JvmArgumentsTests
{
    [Fact]
    public void Build_Memory_SetsMinAndMaxHeapToSameValue()
    {
        var args = JvmArguments.Build(21, 4096, "server.jar");

        Assert.Equal("-Xms4096M", args[0]);
        Assert.Equal("-Xmx4096M", args[1]);
    }

    [Fact]
    public void Build_AnyVersion_EndsWithJarAndNogui()
    {
        var args = JvmArguments.Build(17, 2048, "server.jar");

        Assert.Equal(["-jar", "server.jar", "nogui"], args.TakeLast(3));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(17)]
    public void Build_Java17OrOlder_UsesAikarFlags(int javaMajor)
    {
        var args = JvmArguments.Build(javaMajor, 4096, "server.jar");

        Assert.Contains("-XX:+UseG1GC", args);
        Assert.Contains("-XX:G1HeapRegionSize=8M", args);
        Assert.DoesNotContain("-XX:+UseZGC", args);
    }

    [Fact]
    public void Build_AikarLargeHeap_UsesLargeHeapValues()
    {
        var args = JvmArguments.Build(17, 16384, "server.jar");

        Assert.Contains("-XX:G1HeapRegionSize=16M", args);
        Assert.Contains("-XX:G1NewSizePercent=40", args);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(22)]
    public void Build_Java21To22_UsesGenerationalZgc(int javaMajor)
    {
        var args = JvmArguments.Build(javaMajor, 4096, "server.jar");

        Assert.Contains("-XX:+UseZGC", args);
        Assert.Contains("-XX:+ZGenerational", args);
        Assert.DoesNotContain("-XX:+UseG1GC", args);
    }

    [Theory]
    [InlineData(23)]
    [InlineData(25)]
    public void Build_Java23OrNewer_UsesZgcWithoutGenerationalFlag(int javaMajor)
    {
        var args = JvmArguments.Build(javaMajor, 4096, "server.jar");

        Assert.Contains("-XX:+UseZGC", args);
        Assert.DoesNotContain("-XX:+ZGenerational", args);
    }

    [Fact]
    public void Build_AnyVersion_ForcesUtf8Console()
    {
        var args = JvmArguments.Build(8, 2048, "server.jar");

        Assert.Contains("-Dfile.encoding=UTF-8", args);
        Assert.Contains("-Dsun.stdout.encoding=UTF-8", args);
        Assert.Contains("-Dstdout.encoding=UTF-8", args);
    }
}
