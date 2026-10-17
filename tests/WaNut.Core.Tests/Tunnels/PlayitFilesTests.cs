using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Tunnels;

namespace WaNut.Core.Tests.Tunnels;

public sealed class PlayitFilesTests
{
    [Fact]
    public void SaveSecret_ThenLoad_ReturnsTheKeyInPlayitsTomlForm()
    {
        using var temp = new TempDir();
        var files = new PlayitFiles(new LauncherPaths(temp.Path));

        files.SaveSecret("0123abcd");

        Assert.Equal("0123abcd", files.LoadSecret());
        Assert.Equal("secret_key = \"0123abcd\"\n", File.ReadAllText(files.SecretFile));
    }

    [Fact]
    public void ForgetSecret_RemovesTheKey()
    {
        using var temp = new TempDir();
        var files = new PlayitFiles(new LauncherPaths(temp.Path));
        files.SaveSecret("0123abcd");

        files.ForgetSecret();

        Assert.False(files.HasSecret);
    }

    [Fact]
    public void SaveSecret_NotHex_Throws()
    {
        using var temp = new TempDir();

        Assert.Throws<ArgumentException>(() => new PlayitFiles(new LauncherPaths(temp.Path)).SaveSecret("a\"\nb"));
    }

    [Fact]
    public void ParseRelease_SignedAsset_ReturnsItsUrlAndDigest()
    {
        var release = System.Text.Json.Nodes.JsonNode.Parse("""
            {"tag_name":"v1.0.10","assets":[
              {"name":"playit-windows-x86_64.exe","browser_download_url":"https://example.com/unsigned","digest":"sha256:00"},
              {"name":"playit-windows-x86_64-signed.exe","browser_download_url":"https://example.com/signed","digest":"sha256:2dbd"}]}
            """);

        var (version, url, sha256) = PlayitAgentDownloader.ParseRelease(release);

        Assert.Equal("v1.0.10", version);
        Assert.Equal("https://example.com/signed", url.AbsoluteUri);
        Assert.Equal("2dbd", sha256);
    }

    [Fact]
    public void ParseRelease_NoDigest_Throws()
    {
        var release = System.Text.Json.Nodes.JsonNode.Parse("""
            {"tag_name":"v1.0.10","assets":[{"name":"playit-windows-x86_64-signed.exe","browser_download_url":"https://example.com/signed"}]}
            """);

        Assert.Throws<InvalidDataException>(() => PlayitAgentDownloader.ParseRelease(release));
    }
}
