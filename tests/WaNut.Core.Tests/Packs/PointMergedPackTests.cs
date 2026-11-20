using System.Security.Cryptography;
using WaNut.Core.Packs;
using WaNut.Core.Properties;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Packs;

public sealed class PointMergedPackTests
{
    [Fact]
    public void PointMergedPackAt_WritesAddressSha1AndRequired_KeepsOtherLines()
    {
        using var dir = new TempDir();
        var zip = Path.Combine(dir.Path, "merged.zip");
        File.WriteAllBytes(zip, [1, 2, 3]);
        var properties = Path.Combine(dir.Path, "server.properties");
        File.WriteAllText(properties, "motd=hi\nresource-pack=old\n");

        PackLibrary.PointMergedPackAt(properties, zip, new Uri("http://203.0.113.5:25800/resourcepack.zip"), required: true);

        var saved = PropertiesFile.Load(properties);
        Assert.Equal("http://203.0.113.5:25800/resourcepack.zip", saved.Get("resource-pack"));
        Assert.Equal(Convert.ToHexStringLower(SHA1.HashData([1, 2, 3])), saved.Get("resource-pack-sha1"));
        Assert.Equal("true", saved.Get("require-resource-pack"));
        Assert.Equal("hi", saved.Get("motd"));
    }
}
