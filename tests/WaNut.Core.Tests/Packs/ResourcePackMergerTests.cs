using System.IO.Compression;
using System.Text;
using WaNut.Core.Packs;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Packs;

public sealed class ResourcePackMergerTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string MakePack(string name, params (string Path, string Content)[] files)
    {
        var path = Path.Combine(_dir.Path, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryPath, content) in files)
        {
            var entry = archive.CreateEntry(entryPath);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        return path;
    }

    private static string Read(string zip, string entryPath)
    {
        using var archive = ZipFile.OpenRead(zip);
        using var reader = new StreamReader(archive.GetEntry(entryPath)!.Open());
        return reader.ReadToEnd();
    }

    private string Out => Path.Combine(_dir.Path, "out", "merged.zip");

    [Fact]
    public void Merge_DistinctFiles_CombinesAll()
    {
        var a = MakePack("a.zip", ("pack.mcmeta", "A"), ("assets/minecraft/textures/a.png", "a"));
        var b = MakePack("b.zip", ("pack.mcmeta", "B"), ("assets/minecraft/textures/b.png", "b"));

        var result = ResourcePackMerger.Merge([a, b], Out);

        Assert.Equal(3, result.FileCount);
        Assert.Empty(result.Overlaps);
        Assert.Equal("a", Read(Out, "assets/minecraft/textures/a.png"));
        Assert.Equal("b", Read(Out, "assets/minecraft/textures/b.png"));
    }

    [Fact]
    public void Merge_SameFile_FirstPackWinsAndOverlapIsReported()
    {
        var top = MakePack("top.zip", ("pack.mcmeta", "TOP"), ("assets/minecraft/lang/en_us.json", "top"));
        var low = MakePack("low.zip", ("pack.mcmeta", "LOW"), ("assets/minecraft/lang/en_us.json", "low"));
        var lower = MakePack("lower.zip", ("pack.mcmeta", "LOWER"), ("assets/minecraft/lang/en_us.json", "lower"));

        var result = ResourcePackMerger.Merge([top, low, lower], Out);

        Assert.Equal("top", Read(Out, "assets/minecraft/lang/en_us.json"));
        var overlap = Assert.Single(result.Overlaps);
        Assert.Equal("assets/minecraft/lang/en_us.json", overlap.Path);
        Assert.Equal("top.zip", overlap.Winner);
        Assert.Equal(["low.zip", "lower.zip"], overlap.Losers);
    }

    [Fact]
    public void Merge_PackMcmeta_ComesFromTopPackWithoutWarning()
    {
        var top = MakePack("top.zip", ("pack.mcmeta", "TOP"), ("pack.png", "icon-top"));
        var low = MakePack("low.zip", ("pack.mcmeta", "LOW"), ("pack.png", "icon-low"));

        var result = ResourcePackMerger.Merge([top, low], Out);

        Assert.Equal("TOP", Read(Out, "pack.mcmeta"));
        Assert.Equal("icon-top", Read(Out, "pack.png"));
        Assert.Empty(result.Overlaps);
    }

    [Fact]
    public void Merge_SamePacksTwice_GivesSameSha1()
    {
        var a = MakePack("a.zip", ("pack.mcmeta", "A"), ("assets/x.txt", "x"));
        var b = MakePack("b.zip", ("pack.mcmeta", "B"), ("assets/y.txt", "y"));

        var first = ResourcePackMerger.Merge([a, b], Out);
        var second = ResourcePackMerger.Merge([a, b], Out);

        Assert.Equal(first.Sha1, second.Sha1);
        Assert.Equal(40, first.Sha1.Length);
    }

    [Fact]
    public void Merge_OrderChanged_ChangesWinnerAndSha1()
    {
        var a = MakePack("a.zip", ("pack.mcmeta", "A"), ("assets/x.txt", "from-a"));
        var b = MakePack("b.zip", ("pack.mcmeta", "B"), ("assets/x.txt", "from-b"));

        var ab = ResourcePackMerger.Merge([a, b], Out);
        var ba = ResourcePackMerger.Merge([b, a], Out);

        Assert.Equal("from-b", Read(Out, "assets/x.txt"));
        Assert.NotEqual(ab.Sha1, ba.Sha1);
    }

    [Fact]
    public void Merge_PathsDifferingByCase_CountAsOverlap()
    {
        var a = MakePack("a.zip", ("pack.mcmeta", "A"), ("assets/minecraft/Textures/x.png", "a"));
        var b = MakePack("b.zip", ("pack.mcmeta", "B"), ("assets/minecraft/textures/x.png", "b"));

        var result = ResourcePackMerger.Merge([a, b], Out);

        Assert.Single(result.Overlaps);
        Assert.Equal(2, result.FileCount);
    }

    [Fact]
    public void Merge_UnsafeEntryPath_IsSkipped()
    {
        var a = MakePack("a.zip", ("pack.mcmeta", "A"), ("../evil.txt", "x"), ("assets/ok.txt", "ok"));

        var result = ResourcePackMerger.Merge([a], Out);

        Assert.Equal(2, result.FileCount);
        using var archive = ZipFile.OpenRead(Out);
        Assert.Null(archive.GetEntry("../evil.txt"));
    }

    [Fact]
    public void Merge_NoPackMcmeta_Throws()
    {
        var bad = MakePack("bad.zip", ("assets/x.txt", "x"));

        var error = Assert.Throws<InvalidDataException>(() => ResourcePackMerger.Merge([bad], Out));

        Assert.Contains("bad.zip", error.Message);
        Assert.False(File.Exists(Out));
    }

    [Fact]
    public void Merge_NotAZip_Throws()
    {
        var path = Path.Combine(_dir.Path, "junk.zip");
        File.WriteAllText(path, "not a zip");

        Assert.Throws<InvalidDataException>(() => ResourcePackMerger.Merge([path], Out));
    }

    [Fact]
    public void Merge_NoPacks_Throws() =>
        Assert.Throws<ArgumentException>(() => ResourcePackMerger.Merge([], Out));
}
