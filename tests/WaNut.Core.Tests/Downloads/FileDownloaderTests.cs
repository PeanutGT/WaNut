using System.Text;
using WaNut.Core.Downloads;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Downloads;

public sealed class FileDownloaderTests : IDisposable
{
    private static readonly Uri FileUrl = new("https://example.com/server.jar");
    private static readonly byte[] Content = Encoding.UTF8.GetBytes("pretend this is a server jar");

    private readonly TempDir _temp = new();
    private readonly StubHttpHandler _http = new StubHttpHandler().Add(FileUrl, Content);

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task DownloadAsync_MatchingSha1_SavesFile()
    {
        var target = Path.Combine(_temp.Path, "sub", "server.jar");

        await new FileDownloader(_http.CreateClient()).DownloadAsync(FileUrl, target, ExpectedHash.Sha1(Hashes.Sha1(Content)));

        Assert.Equal(Content, await File.ReadAllBytesAsync(target));
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public async Task DownloadAsync_MatchingSha256UpperCase_SavesFile()
    {
        var target = Path.Combine(_temp.Path, "server.jar");

        await new FileDownloader(_http.CreateClient()).DownloadAsync(FileUrl, target, ExpectedHash.Sha256(Hashes.Sha256(Content).ToUpperInvariant()));

        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task DownloadAsync_WrongHash_ThrowsAndLeavesNoFile()
    {
        var target = Path.Combine(_temp.Path, "server.jar");

        await Assert.ThrowsAsync<DownloadVerificationException>(() =>
            new FileDownloader(_http.CreateClient()).DownloadAsync(FileUrl, target, ExpectedHash.Sha1(new string('0', 40))));

        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public async Task DownloadAsync_WrongHash_KeepsExistingFile()
    {
        var target = Path.Combine(_temp.Path, "server.jar");
        await File.WriteAllTextAsync(target, "old good file");

        await Assert.ThrowsAsync<DownloadVerificationException>(() =>
            new FileDownloader(_http.CreateClient()).DownloadAsync(FileUrl, target, ExpectedHash.Sha1(new string('0', 40))));

        Assert.Equal("old good file", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task DownloadAsync_HttpUrl_IsRejected()
    {
        var target = Path.Combine(_temp.Path, "server.jar");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new FileDownloader(_http.CreateClient()).DownloadAsync(new Uri("http://example.com/server.jar"), target, ExpectedHash.Sha1(Hashes.Sha1(Content))));

        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task DownloadAsync_WithProgress_ReportsAllBytes()
    {
        var reports = new List<DownloadProgress>();
        var progress = new SyncProgressForTests<DownloadProgress>(reports.Add);

        await new FileDownloader(_http.CreateClient()).DownloadAsync(FileUrl, Path.Combine(_temp.Path, "f"), ExpectedHash.Sha1(Hashes.Sha1(Content)), progress);

        Assert.NotEmpty(reports);
        Assert.Equal(Content.Length, reports[^1].BytesReceived);
        Assert.Equal(1.0, reports[^1].Fraction);
    }

    [Fact]
    public async Task DownloadBytesAsync_WrongHash_Throws()
    {
        await Assert.ThrowsAsync<DownloadVerificationException>(() =>
            new FileDownloader(_http.CreateClient()).DownloadBytesAsync(FileUrl, ExpectedHash.Sha1(new string('0', 40))));
    }

    private sealed class SyncProgressForTests<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
