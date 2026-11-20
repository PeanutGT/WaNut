using System.Net;
using WaNut.Core.Remote;
using WaNut.Core.Remote.Run;
using WaNut.Core.Tests.Hotplug;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Remote.Run;

public sealed class VpsPackHostTests
{
    [Theory]
    [InlineData("port 25800", true, 25800, false)]
    [InlineData("noise\nport 25899\n", true, 25899, false)]
    [InlineData("unsupported", false, 0, true)]
    [InlineData("failed", false, 0, false)]
    [InlineData("port 80", false, 0, false)]
    [InlineData("", false, 0, false)]
    public void ParseStart_Output_ReadsPortOrReason(string output, bool started, int port, bool unsupported) =>
        Assert.Equal((started, port, unsupported), VpsPackHost.ParseStart(output));

    [Theory]
    [InlineData("203.0.113.5", "http://203.0.113.5:25800/resourcepack.zip")]
    [InlineData("mc.example.com", "http://mc.example.com:25800/resourcepack.zip")]
    [InlineData("2001:db8::1", "http://[2001:db8::1]:25800/resourcepack.zip")]
    [InlineData("[2001:db8::1]", "http://[2001:db8::1]:25800/resourcepack.zip")]
    public void UrlFor_HostOrAddress_IsPlainHttp(string host, string expected) =>
        Assert.Equal(expected, VpsPackHost.UrlFor(host, 25800).AbsoluteUri);

    [Fact]
    public void StartCommand_QuotesTheServerId()
    {
        var command = VpsPackHost.StartCommand("a b'c", "wanut/servers/a b'c");
        Assert.DoesNotContain("a b'c/", command, StringComparison.Ordinal);
        Assert.Contains("'wanut/packhost/a b'\\''c'", command, StringComparison.Ordinal);
    }

    /// <summary>The host script through this machine's sh and python3: serves the zip, then ends with the server's run.</summary>
    [Fact]
    [Trait("Category", "Ssh")]
    public async Task StartCommand_LocalSh_ServesZipAndStopsWhenRunEnds()
    {
        if (Environment.GetEnvironmentVariable("WANUT_TEST_LOCAL_SH") != "1")
        {
            return;
        }

        using var dir = new TempDir();
        var home = dir.Path;
        const string id = "srv1";
        var www = Path.Combine(home, "wanut", "packhost", id, "www");
        Directory.CreateDirectory(www);
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(Path.Combine(www, VpsPackHost.ZipName), bytes);
        var run = Path.Combine(home, "wanut", "servers", id, ".wanut-run");
        Directory.CreateDirectory(run);

        // Left over from the last run: must not stop this one.
        await File.WriteAllTextAsync(Path.Combine(run, "exit"), "0");
        await File.WriteAllTextAsync(Path.Combine(run, "java.pid"), "1");
        await Task.Delay(1100);

        var runner = new LocalShRunner();
        var result = await runner.RunAsync($"export HOME={RemoteShell.Quote(home)}; {VpsPackHost.StartCommand(id, $"wanut/servers/{id}")}");
        var (started, port, _) = VpsPackHost.ParseStart(result.Output);
        Assert.True(started, result.Output + result.Error);

        using var http = new HttpClient();
        Assert.Equal(bytes, await http.GetByteArrayAsync($"http://127.0.0.1:{port}/{VpsPackHost.ZipName}"));

        // The server's run ends: the exit file is newer than the host's start.
        await File.WriteAllTextAsync(Path.Combine(run, "exit"), "0");
        var pidFile = Path.Combine(home, "wanut", "packhost", id, "pid");
        for (var i = 0; i < 30 && File.Exists(pidFile); i++)
        {
            await Task.Delay(500);
        }

        Assert.False(File.Exists(pidFile));
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetByteArrayAsync($"http://127.0.0.1:{port}/{VpsPackHost.ZipName}"));
    }
}
