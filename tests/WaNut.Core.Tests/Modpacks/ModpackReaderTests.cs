using System.Text;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Modpacks;

namespace WaNut.Core.Tests.Modpacks;

public sealed class ModpackReaderTests
{
    internal static readonly string Sha1 = new('a', 40);
    internal static readonly string Sha512 = new('b', 128);

    internal static string File(
        string path = "mods/lithium.jar",
        string server = "required",
        string url = "https://cdn.modrinth.com/data/gvQqBUqZ/versions/1/lithium.jar",
        string? sha512 = null,
        string? sha1 = null) =>
        $$"""
        {"path":"{{path}}","hashes":{"sha1":"{{sha1 ?? Sha1}}","sha512":"{{sha512 ?? Sha512}}"},
         "env":{"client":"required","server":"{{server}}"},"downloads":["{{url}}"],"fileSize":10}
        """;

    internal static string Index(string files = "[]", string dependencies = """{"minecraft":"1.21.1","fabric-loader":"0.16.10"}""") =>
        $$"""
        {"formatVersion":1,"game":"minecraft","versionId":"2.3.0","name":"Cozy Pack","summary":"Hi",
         "files":{{files}},"dependencies":{{dependencies}}}
        """;

    private static Modpack Parse(string json) => ModpackReader.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Parse_FabricPack_ReadsVersionsAndFiles()
    {
        var pack = Parse(Index($"[{File()}]"));

        Assert.Equal("Cozy Pack", pack.Name);
        Assert.Equal("2.3.0", pack.Version);
        Assert.Equal("1.21.1", pack.MinecraftVersion);
        Assert.Equal(ServerType.Fabric, pack.Type);
        Assert.Equal("0.16.10", pack.LoaderVersion);
        var file = Assert.Single(pack.Files);
        Assert.Equal("mods/lithium.jar", file.Path);
        Assert.Equal(ExpectedHash.Sha512(Sha512), file.Hash);
    }

    [Fact]
    public void Parse_NoLoader_IsVanilla()
    {
        var pack = Parse(Index(dependencies: """{"minecraft":"1.20.1"}"""));

        Assert.Equal(ServerType.Vanilla, pack.Type);
        Assert.Null(pack.LoaderVersion);
    }

    [Fact]
    public void Parse_ServerUnsupportedFile_IsSkippedAndCounted()
    {
        var pack = Parse(Index($"[{File()},{File("mods/sodium.jar", server: "unsupported")},{File("mods/opt.jar", server: "optional")}]"));

        Assert.Equal(["mods/lithium.jar", "mods/opt.jar"], pack.Files.Select(f => f.Path));
        Assert.Equal(1, pack.SkippedFiles);
    }

    [Fact]
    public void Parse_NoSha512_FallsBackToSha1()
    {
        var json = Index($"[{File()}]").Replace($",\"sha512\":\"{Sha512}\"", string.Empty, StringComparison.Ordinal);

        Assert.Equal(ExpectedHash.Sha1(Sha1), Assert.Single(Parse(json).Files).Hash);
    }

    [Fact]
    public void Parse_QuiltPack_ThrowsUnsupportedLoader()
    {
        var e = Assert.Throws<ModpackException>(() => Parse(Index(dependencies: """{"minecraft":"1.20.1","quilt-loader":"0.26.0"}""")));

        Assert.Equal(ModpackProblem.UnsupportedLoader, e.Problem);
        Assert.Equal("quilt-loader", e.Detail);
    }

    [Theory]
    [InlineData("forge", "47.3.0", ServerType.Forge)]
    [InlineData("neoforge", "21.1.209", ServerType.NeoForge)]
    public void Parse_ForgeOrNeoForgePack_ReadsTypeAndLoaderVersion(string key, string version, ServerType expected)
    {
        var pack = Parse(Index(dependencies: $$"""{"minecraft":"1.20.1","{{key}}":"{{version}}"}"""));

        Assert.Equal(expected, pack.Type);
        Assert.Equal(version, pack.LoaderVersion);
    }

    [Fact]
    public void Parse_TwoLoaders_ThrowsNotAModpack()
    {
        var e = Assert.Throws<ModpackException>(() =>
            Parse(Index(dependencies: """{"minecraft":"1.20.1","forge":"47.3.0","fabric-loader":"0.16.10"}""")));

        Assert.Equal(ModpackProblem.NotAModpack, e.Problem);
    }

    [Fact]
    public void Parse_ForgeVersionWithSlash_ThrowsNotAModpack()
    {
        var e = Assert.Throws<ModpackException>(() => Parse(Index(dependencies: """{"minecraft":"1.20.1","forge":"../47"}""")));

        Assert.Equal(ModpackProblem.NotAModpack, e.Problem);
    }

    [Theory]
    [InlineData("../evil.jar")]
    [InlineData("mods/../../evil.jar")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("/etc/evil")]
    [InlineData("mods//evil.jar")]
    public void Parse_PathOutsideServer_ThrowsUnsafeFile(string path)
    {
        var e = Assert.Throws<ModpackException>(() => Parse(Index($"[{File(path)}]")));

        Assert.Equal(ModpackProblem.UnsafeFile, e.Problem);
    }

    [Theory]
    [InlineData("http://cdn.modrinth.com/data/x.jar")]
    [InlineData("https://evil.example.com/x.jar")]
    [InlineData("https://cdn.modrinth.com:8443/x.jar")]
    public void Parse_DownloadFromOtherHost_ThrowsUnsafeFile(string url)
    {
        var e = Assert.Throws<ModpackException>(() => Parse(Index($"[{File(url: url)}]")));

        Assert.Equal(ModpackProblem.UnsafeFile, e.Problem);
    }

    [Fact]
    public void Parse_NoUsableHash_ThrowsUnsafeFile()
    {
        var e = Assert.Throws<ModpackException>(() => Parse(Index($"[{File(sha512: "zz", sha1: "short")}]")));

        Assert.Equal(ModpackProblem.UnsafeFile, e.Problem);
    }

    [Theory]
    [InlineData("""{"formatVersion":2,"game":"minecraft","dependencies":{"minecraft":"1.21.1"}}""")]
    [InlineData("""{"formatVersion":1,"game":"other","dependencies":{"minecraft":"1.21.1"}}""")]
    [InlineData("""{"formatVersion":1,"game":"minecraft","dependencies":{}}""")]
    [InlineData("""{"formatVersion":"1","game":"minecraft","dependencies":{"minecraft":"1.21.1"}}""")]
    [InlineData("""{"formatVersion":1,"game":"minecraft","dependencies":{"minecraft":"1.21.1","fabric-loader":"0.16\ngame-version=x"}}""")]
    [InlineData("not json")]
    public void Parse_NotAPack_ThrowsNotAModpack(string json)
    {
        var e = Assert.Throws<ModpackException>(() => Parse(json));

        Assert.Equal(ModpackProblem.NotAModpack, e.Problem);
    }

    [Fact]
    public void Read_ZipWithoutIndex_ThrowsNotAModpack()
    {
        using var dir = new TestSupport.TempDir();
        var path = Path.Combine(dir.Path, "empty.mrpack");
        using (System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
        }

        Assert.Equal(ModpackProblem.NotAModpack, Assert.Throws<ModpackException>(() => ModpackReader.Read(path)).Problem);
    }

    [Fact]
    public void Read_NotAZip_ThrowsNotAModpack()
    {
        using var dir = new TestSupport.TempDir();
        var path = Path.Combine(dir.Path, "text.mrpack");
        System.IO.File.WriteAllText(path, "hello");

        Assert.Equal(ModpackProblem.NotAModpack, Assert.Throws<ModpackException>(() => ModpackReader.Read(path)).Problem);
    }
}
