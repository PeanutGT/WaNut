using System.Net;
using System.Net.Http.Headers;
using WaNut.Core.Downloads;
using WaNut.Core.Mods;
using WaNut.Core.Packs;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Packs;

public sealed class ResourcePackLinkTests : IDisposable
{
    private static readonly Uri Link = new("https://files.example.com/pack.zip");

    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData("https://files.example.com/pack.zip", "https://files.example.com/pack.zip")]
    [InlineData("  http://host/pack.zip  ", "http://host/pack.zip")]
    [InlineData("https://www.dropbox.com/s/abc/pack.zip?dl=0", "https://www.dropbox.com/s/abc/pack.zip?dl=1")]
    [InlineData("https://www.dropbox.com/s/abc/pack.zip", "https://www.dropbox.com/s/abc/pack.zip?dl=1")]
    [InlineData("https://www.dropbox.com/scl/fi/x/pack.zip?rlkey=k&dl=0", "https://www.dropbox.com/scl/fi/x/pack.zip?rlkey=k&dl=1")]
    public void Parse_WebAddress_ReturnsDownloadAddress(string text, string expected) =>
        Assert.Equal(expected, ResourcePackLink.Parse(text)!.AbsoluteUri);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a link")]
    [InlineData("ftp://host/pack.zip")]
    [InlineData("C:\\packs\\pack.zip")]
    public void Parse_NotAWebAddress_ReturnsNull(string? text) => Assert.Null(ResourcePackLink.Parse(text));

    [Fact]
    public void Sha1Of_File_IsLowerCaseHex()
    {
        var path = Path.Combine(_dir.Path, "a.zip");
        File.WriteAllText(path, "abc");

        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", ResourcePackLink.Sha1Of(path));
    }

    [Fact]
    public async Task CheckAsync_SameZip_ReturnsMatch()
    {
        using var http = new HttpClient(new Reply(HttpStatusCode.OK, "application/zip", "abc"));

        var check = await ResourcePackLink.CheckAsync(http, Link, "A9993E364706816ABA3E25717850C26C9CD0D89D");

        Assert.Equal(ResourcePackLinkStatus.Match, check.Status);
    }

    [Fact]
    public async Task CheckAsync_OtherFile_ReturnsDifferent()
    {
        using var http = new HttpClient(new Reply(HttpStatusCode.OK, "application/zip", "older upload"));

        var check = await ResourcePackLink.CheckAsync(http, Link, "a9993e364706816aba3e25717850c26c9cd0d89d");

        Assert.Equal(ResourcePackLinkStatus.Different, check.Status);
    }

    [Fact]
    public async Task CheckAsync_HtmlPage_ReturnsWebPage()
    {
        using var http = new HttpClient(new Reply(HttpStatusCode.OK, "text/html", "<html>share page</html>"));

        var check = await ResourcePackLink.CheckAsync(http, Link, "a9993e364706816aba3e25717850c26c9cd0d89d");

        Assert.Equal(ResourcePackLinkStatus.WebPage, check.Status);
    }

    [Fact]
    public async Task CheckAsync_NotFound_ReturnsUnreachableWithStatus()
    {
        using var http = new HttpClient(new Reply(HttpStatusCode.NotFound, "text/plain", string.Empty));

        var check = await ResourcePackLink.CheckAsync(http, Link, "a9993e364706816aba3e25717850c26c9cd0d89d");

        Assert.Equal(ResourcePackLinkStatus.Unreachable, check.Status);
        Assert.Equal("HTTP 404", check.Detail);
    }

    [Fact]
    public async Task CheckAsync_ConnectionFails_ReturnsUnreachable()
    {
        using var http = new HttpClient(new Failing());

        var check = await ResourcePackLink.CheckAsync(http, Link, "a9993e364706816aba3e25717850c26c9cd0d89d");

        Assert.Equal(ResourcePackLinkStatus.Unreachable, check.Status);
    }

    [Fact]
    public void SetCustomPackUrl_Address_IsRemembered_AndClearedByNull()
    {
        var library = new PackLibrary(new ModrinthClient(new HttpClient()), new FileDownloader(new HttpClient()));

        library.SetCustomPackUrl(_dir.Path, Link);
        Assert.Equal(Link, library.GetCustomPackUrl(_dir.Path));

        library.SetCustomPackUrl(_dir.Path, null);
        Assert.Null(library.GetCustomPackUrl(_dir.Path));
    }

    private sealed class Reply(HttpStatusCode status, string mediaType, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StringContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }

    private sealed class Failing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no route");
    }
}
