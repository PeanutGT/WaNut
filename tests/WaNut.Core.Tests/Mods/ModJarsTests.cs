using System.IO.Compression;
using WaNut.Core.Mods;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Mods;

public sealed class ModJarsTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private void Jar(string fileName, string entry, string content)
    {
        using var zip = ZipFile.Open(Path.Combine(_dir.Path, fileName), ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
        writer.Write(content);
    }

    [Fact]
    public void Read_FabricForgeAndNeoForgeJars_ReadsIdNameAndVersion()
    {
        Jar("sodium.jar", "fabric.mod.json", """{"schemaVersion":1,"id":"sodium","name":"Sodium","version":"0.5.3",}""");
        Jar("jei.jar", "META-INF/mods.toml", "modLoader=\"javafml\"\n[[mods]]\nmodId=\"jei\"\nversion=\"${file.jarVersion}\"\ndisplayName=\"Just Enough Items\"\n");
        Jar("create.jar", "META-INF/neoforge.mods.toml", "[[mods]]\n  modId = \"create\"\n  version = \"6.0.1\"\n");
        File.WriteAllText(Path.Combine(_dir.Path, "broken.jar"), "not a zip");

        var jars = ModJars.Read(_dir.Path).OrderBy(j => j.Id).ToList();

        Assert.Equal(
            [new ModJar("create.jar", "create", null, "6.0.1"), new ModJar("jei.jar", "jei", "Just Enough Items", null), new ModJar("sodium.jar", "sodium", "Sodium", "0.5.3")],
            jars);
    }

    [Fact]
    public void Find_ByIdNameOrFile_FindsTheJar()
    {
        ModJar[] jars = [new("a.jar", "jei", "Just Enough Items", null), new("b.jar", "sodium", "Sodium", null)];

        Assert.Equal("a.jar", Assert.Single(ModJars.Find(jars, "JEI")).FileName);
        Assert.Equal("a.jar", Assert.Single(ModJars.Find(jars, "Just Enough Items")).FileName);
        Assert.Equal("b.jar", Assert.Single(ModJars.Find(jars, "b.jar")).FileName);
    }

    [Theory]
    [InlineData("0.5.10", "0.5.9", 1)]
    [InlineData("1.20.1-15.2.0", "1.20.1-15.10.0", -1)]
    [InlineData("1.0", "1.0", 0)]
    [InlineData("1.0.1", "1.0", 1)]
    public void CompareVersions_NumberParts_CompareAsNumbers(string a, string b, int expected)
    {
        Assert.Equal(expected, Math.Sign(ModJars.CompareVersions(a, b)));
    }

    [Fact]
    public void Newest_TwoCopies_PicksHigherVersion()
    {
        ModJar[] copies = [new("jei-15.10.jar", "jei", null, "15.10.0"), new("jei-15.2.jar", "jei", null, "15.2.0")];

        Assert.Equal("jei-15.10.jar", ModJars.Newest(copies).FileName);
    }
}
