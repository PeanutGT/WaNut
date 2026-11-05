using WaNut.Core.Remote;

namespace WaNut.Core.Tests.Remote;

public sealed class HostProbeTests
{
    private const string Ubuntu = """
        os=Linux
        machine=x86_64
        distro=ubuntu
        like=debian
        name=Ubuntu 24.04.1 LTS
        memkb=8131072
        diskkb=41943040
        has=curl
        has=tar

        """;

    [Fact]
    public void Parse_Ubuntu_ReadsEverythingAndHasNoProblems()
    {
        var info = HostProbe.Parse(Ubuntu);

        Assert.Equal("Linux", info.Os);
        Assert.Equal(HostArchitecture.X64, info.Architecture);
        Assert.Equal("ubuntu", info.DistroId);
        Assert.Equal(["debian"], info.DistroLike);
        Assert.Equal("Ubuntu 24.04.1 LTS", info.DistroName);
        Assert.Equal(8131072L * 1024, info.MemoryBytes);
        Assert.Equal(40L * 1024 * 1024 * 1024, info.FreeDiskBytes);
        Assert.True(info.HasCurl);
        Assert.False(info.HasWget);
        Assert.True(info.HasTar);
        Assert.True(info.IsSupportedDistro);
        Assert.Empty(info.Problems);
    }

    [Fact]
    public void Parse_WindowsLineEndingsAndQuotes_StillReads()
    {
        var info = HostProbe.Parse("os=Linux\r\ndistro=\"debian\"\r\nname=\"Debian GNU/Linux 12 (bookworm)\"\r\n");

        Assert.Equal("debian", info.DistroId);
        Assert.Equal("Debian GNU/Linux 12 (bookworm)", info.DistroName);
    }

    [Fact]
    public void Parse_SmallArmBoxWithoutTools_ListsEachProblem()
    {
        var info = HostProbe.Parse("os=Linux\nmachine=aarch64\ndistro=alpine\nmemkb=1000000\ndiskkb=1000\n");

        Assert.Equal(HostArchitecture.Arm64, info.Architecture);
        Assert.False(info.IsSupportedDistro);
        Assert.Equal([HostProblem.LowMemory, HostProblem.LowDisk, HostProblem.NoDownloader, HostProblem.NoTar], info.Problems);
    }

    [Fact]
    public void Parse_NotLinux_SaysSo()
    {
        var info = HostProbe.Parse("os=FreeBSD\nmachine=riscv64\nmemkb=99999999\ndiskkb=99999999\nhas=wget\nhas=tar\n");

        Assert.Equal([HostProblem.NotLinux, HostProblem.UnsupportedCpu], info.Problems);
    }

    [Fact]
    public void Script_HasNoSingleQuote_SoItFitsInShDashC()
    {
        Assert.DoesNotContain('\'', HostProbe.Script);
        Assert.StartsWith("sh -c '", HostProbe.Command, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("x86_64", HostArchitecture.X64)]
    [InlineData("amd64", HostArchitecture.X64)]
    [InlineData("aarch64", HostArchitecture.Arm64)]
    [InlineData("armv7l", HostArchitecture.Other)]
    public void ArchitectureOf_KnownNames(string machine, HostArchitecture expected)
    {
        Assert.Equal(expected, HostProbe.ArchitectureOf(machine));
    }
}
