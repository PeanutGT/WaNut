using System.Text;
using WaNut.Core.Java;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Java;

public sealed class AdoptiumClientTests
{
    [Fact]
    public void ParseLatest_WindowsJre_ReturnsZipPackage()
    {
        var json = SampleData.AdoptiumLatest(21, "https://github.com/adoptium/temurin21-binaries/releases/download/x/jre.zip", "abcd", 47_000_000);

        var package = AdoptiumClient.ParseLatest(21, new MemoryStream(Encoding.UTF8.GetBytes(json)));

        Assert.Equal(21, package.Major);
        Assert.Equal("jdk-21.0.1+1", package.ReleaseName);
        Assert.Equal(new Uri("https://github.com/adoptium/temurin21-binaries/releases/download/x/jre.zip"), package.Link);
        Assert.Equal("abcd", package.Sha256);
    }

    [Fact]
    public void ParseLatest_EmptyList_Throws()
    {
        Assert.Throws<InvalidDataException>(() => AdoptiumClient.ParseLatest(21, new MemoryStream("[]"u8.ToArray())));
    }

    [Fact]
    public void LatestJreUrl_Major_AsksForWindowsX64Jre()
    {
        var url = AdoptiumClient.LatestJreUrl(17).AbsoluteUri;

        Assert.StartsWith("https://api.adoptium.net/v3/assets/latest/17/hotspot?", url);
        Assert.Contains("os=windows", url);
        Assert.Contains("architecture=x64", url);
        Assert.Contains("image_type=jre", url);
    }
}
