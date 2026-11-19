using WaNut.Core.Hotplug.Preflight;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Hotplug.Preflight;

public sealed class DatapackInspectorTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string Zip(string name, string? meta = PackZip.Meta, params string[] entries) => PackZip.Make(Path.Combine(_dir.Path, name), meta, entries);

    [Fact]
    public async Task InspectAsync_LogicOnly_SafeAndMatching()
    {
        var path = Zip("loot.zip", PackZip.Meta, "data/ev/loot_table/chest.json", "data/ev/function/tick.mcfunction", "data/minecraft/tags/function/tick.json");
        var result = await DatapackInspector.InspectAsync(path, 48);
        Assert.Equal((PackRisk.Safe, FormatCheck.Matches, false, false), (result.Risk, result.Format, result.IsBlocked, result.NeedsConfirmation));
        Assert.Equal(64, result.Sha256.Length);
        Assert.Equal(await DatapackInspector.Sha256Async(path), result.Sha256);
    }

    [Fact]
    public async Task InspectAsync_Worldgen_AppliesAfterRestartNeedsConfirmation()
    {
        var result = await DatapackInspector.InspectAsync(Zip("terrain.zip", PackZip.Meta, "data/ev/worldgen/biome/x.json", "data/ev/enchantment/y.json"), 48);
        Assert.Equal(PackRisk.AppliesAfterRestart, result.Risk);
        Assert.Equal(["enchantment", "worldgen"], result.RiskyFolders);
        Assert.True(result.NeedsConfirmation);
    }

    [Theory]
    [InlineData("data/ev/dimension/moon.json")]
    [InlineData("data/ev/dimension_type/moon.json")]
    [InlineData("overlay_48/data/ev/dimension/moon.json")]
    public async Task InspectAsync_Dimension_Blocked(string entry)
    {
        var result = await DatapackInspector.InspectAsync(Zip("moon.zip", PackZip.Meta, entry), 48);
        Assert.Equal(PackRisk.NeedsRestart, result.Risk);
        Assert.True(result.IsBlocked);
    }

    [Fact]
    public async Task InspectAsync_OtherFormat_NeedsConfirmation()
    {
        var result = await DatapackInspector.InspectAsync(Zip("old.zip", """{"pack":{"pack_format":15}}""", "data/ev/recipe/a.json"), 48);
        Assert.Equal(FormatCheck.Mismatch, result.Format);
        Assert.True(result.NeedsConfirmation);
    }

    [Fact]
    public async Task InspectAsync_NoMeta_Blocked()
    {
        var result = await DatapackInspector.InspectAsync(Zip("empty.zip", null, "data/ev/recipe/a.json"), 48);
        Assert.True(result.IsBlocked);
        Assert.Contains("pack.mcmeta", result.Problem);
    }

    [Fact]
    public async Task InspectAsync_MetaInsideFolder_SaysSo()
    {
        var result = await DatapackInspector.InspectAsync(Zip("nested.zip", null, "pack/pack.mcmeta"), 48);
        Assert.Contains("inside a folder", result.Problem);
    }

    [Fact]
    public async Task InspectAsync_NotAZip_Blocked()
    {
        var path = Path.Combine(_dir.Path, "fake.zip");
        File.WriteAllText(path, "hello");
        Assert.True((await DatapackInspector.InspectAsync(path, 48)).IsBlocked);
    }

    [Theory]
    [InlineData("pack.json")]
    [InlineData(".hidden.zip")]
    [InlineData("quote\".zip")]
    [InlineData("[x].zip")]
    public async Task InspectAsync_BadName_Blocked(string name)
    {
        var result = await DatapackInspector.InspectAsync(Zip(name), 48);
        Assert.True(result.IsBlocked);
    }

    [Theory]
    [InlineData("data/ns/loot_table/a.json", "loot_table")]
    [InlineData("ov/data/ns/dimension/a.json", "dimension")]
    [InlineData("data/ns/a.json", null)]
    [InlineData("assets/ns/textures/a.png", null)]
    [InlineData("a/b/data/ns/dimension/x.json", null)]
    public void DataFolder_ReadsThirdSegment(string entry, string? folder) => Assert.Equal(folder, DatapackInspector.DataFolder(entry));
}
