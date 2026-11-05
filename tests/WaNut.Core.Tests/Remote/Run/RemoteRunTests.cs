using System.Text;
using WaNut.Core.Java;
using WaNut.Core.Remote;
using WaNut.Core.Remote.Run;

namespace WaNut.Core.Tests.Remote.Run;

public sealed class RemoteRunTests
{
    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("$HOME `x` \"y\"", "'$HOME `x` \"y\"'")]
    public void Quote_AnyText_IsOneLiteralWord(string value, string expected) =>
        Assert.Equal(expected, RemoteShell.Quote(value));

    [Fact]
    public void Home_RelativePath_StaysUnderHome() =>
        Assert.Equal("\"$HOME\"/'wanut/servers/a b'", RemoteShell.Home("/wanut/servers/a b"));

    [Theory]
    [InlineData("@libraries/net/neoforged/neoforge/21.1.1/win_args.txt", "@libraries/net/neoforged/neoforge/21.1.1/unix_args.txt")]
    [InlineData("server.jar", "server.jar")]
    [InlineData("forge-1.16.5-36.2.39.jar", "forge-1.16.5-36.2.39.jar")]
    public void ForLinux_ArgumentFile_UsesTheUnixOne(string jar, string expected) =>
        Assert.Equal(expected, VpsLaunchPlanner.ForLinux(jar));

    [Theory]
    [InlineData(HostArchitecture.X64, "x64")]
    [InlineData(HostArchitecture.Arm64, "aarch64")]
    public void AdoptiumArchitecture_KnownCpu_UsesAdoptiumName(HostArchitecture cpu, string expected) =>
        Assert.Equal(expected, RemoteJava.AdoptiumArchitecture(cpu));

    [Fact]
    public void AdoptiumArchitecture_OtherCpu_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RemoteJava.AdoptiumArchitecture(HostArchitecture.Other));

    [Fact]
    public void ParseLatest_LinuxArm_ReturnsTarGz()
    {
        const string json = """
            [{"binary": {"architecture": "aarch64", "image_type": "jre", "os": "linux",
              "package": {"checksum": "ab", "link": "https://example.com/jre.tar.gz", "name": "OpenJDK21U-jre_aarch64_linux_hotspot.tar.gz", "size": 5}},
              "release_name": "jdk-21.0.5+11"}]
            """;

        var package = AdoptiumClient.ParseLatest(21, new MemoryStream(Encoding.UTF8.GetBytes(json)), "linux", "aarch64");

        Assert.Equal("https://example.com/jre.tar.gz", package.Link.AbsoluteUri);
        Assert.Contains("os=linux", AdoptiumClient.LatestJreUrl(21, "linux", "aarch64").Query, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => AdoptiumClient.ParseLatest(21, new MemoryStream(Encoding.UTF8.GetBytes(json))));
    }

    [Fact]
    public void ParseUsage_ProcOutput_ReturnsMemoryCpuAndCores()
    {
        var parsed = RemoteServer.ParseUsage("250\n2048000\n100\n4\n");

        Assert.NotNull(parsed);
        Assert.Equal(2048000L * 1024, parsed.Value.Usage.MemoryBytes);
        Assert.Equal(TimeSpan.FromSeconds(2.5), parsed.Value.Usage.CpuTime);
        Assert.Equal(4, parsed.Value.Cores);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1\n2\n0\n4")]
    [InlineData("x\n2\n100\n4")]
    public void ParseUsage_Incomplete_ReturnsNull(string output) => Assert.Null(RemoteServer.ParseUsage(output));

    [Fact]
    public void LineSplitter_SplitAcrossReads_CountsBytesOfWholeLinesOnly()
    {
        var splitter = new RemoteServer.LineSplitter();
        var bytes = Encoding.UTF8.GetBytes("héllo\r\nwor");

        var first = splitter.Push(bytes).ToList();
        var second = splitter.Push("ld\n"u8).ToList();

        Assert.Equal(["héllo"], first);
        Assert.Equal(["world"], second);
        Assert.Equal(bytes.Length + 3, splitter.Consumed);
    }

    [Fact]
    public void StartScript_Arguments_AreQuotedWords()
    {
        var script = RunnerScript.StartScript("wanut/servers/x", "wanut/java/java-21/bin/java", ["-Xmx2G", "it's $HOME"]);

        Assert.EndsWith("\"$HOME\"/'wanut/java/java-21/bin/java' '-Xmx2G' 'it'\\''s $HOME'\n", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Unit_PathsWithSpecialCharacters_UseSystemdEscapes()
    {
        var unit = RunnerScript.Unit("/home/a b", "steve", "wanut/servers/100%");

        Assert.Contains("User=steve", unit, StringComparison.Ordinal);
        Assert.Contains("Environment=\"HOME=/home/a b\"", unit, StringComparison.Ordinal);
        Assert.Contains("ExecStart=/bin/sh \"/home/a b/wanut/servers/100%%/.wanut-run/start.sh\"", unit, StringComparison.Ordinal);
        Assert.DoesNotContain("[Install]", unit, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ubuntu", true)]
    [InlineData("_svc-1.x", true)]
    [InlineData("Root", false)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    public void IsPlainUserName_Names_MatchSystemdRules(string user, bool expected) =>
        Assert.Equal(expected, RunnerScript.IsPlainUserName(user));

    [Fact]
    public void UnitName_OddFolder_IsSafe() =>
        Assert.Equal("wanut-ubuntu-my-world.service", RunnerScript.UnitName("ubuntu", "wanut/servers/my world"));

    [Theory]
    [InlineData("running 4242\n", VpsRunState.Running, 4242, -1)]
    [InlineData("exit 0\n", VpsRunState.Ended, 0, 0)]
    [InlineData("exit 137", VpsRunState.Ended, 0, 137)]
    [InlineData("exit", VpsRunState.Ended, 0, -1)]
    [InlineData("none\n", VpsRunState.None, 0, -1)]
    [InlineData("running 1", VpsRunState.None, 0, -1)]
    [InlineData("running x", VpsRunState.None, 0, -1)]
    [InlineData("", VpsRunState.None, 0, -1)]
    public void VpsRunParse_ProbeOutput_ReadsStateAndNumbers(string output, VpsRunState state, int pid, int code) =>
        Assert.Equal(new VpsRun(state, pid, code), VpsRun.Parse(output));
}
