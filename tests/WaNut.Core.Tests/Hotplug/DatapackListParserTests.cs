using WaNut.Core.Hotplug;

namespace WaNut.Core.Tests.Hotplug;

public sealed class DatapackListParserTests
{
    private const string Modern = "[12:00:01] [Server thread/INFO]: There are 3 data pack(s) enabled: [vanilla (built-in)], [file/loot.zip (world)], [file/pack (1).zip (world)]";
    private const string Old = "[12:00:01] [Server thread/INFO]: There are 2 data packs enabled: [vanilla], [file/loot.zip]";

    [Theory]
    [InlineData(Modern, true)]
    [InlineData(Old, true)]
    [InlineData("[12:00:01 INFO]: There are no data packs enabled", true)]
    [InlineData("[12:00:01] [Server thread/INFO]: There are 1 data pack(s) available: [file/off.zip (world)]", false)]
    [InlineData("[12:00:01] [Server thread/INFO]: <Steve> There are 2 data packs enabled: [file/x.zip]", false)]
    [InlineData("[12:00:01] [Server thread/INFO]: Reloading!", false)]
    public void IsAnswer_Enabled(string line, bool expected) => Assert.Equal(expected, DatapackListParser.IsAnswer(line));

    [Fact]
    public void Packs_StripsSourceKeepsParentheses() =>
        Assert.Equal(["vanilla", "file/loot.zip", "file/pack (1).zip"], DatapackListParser.Packs(Modern));

    [Fact]
    public void Packs_OldFormat() => Assert.Equal(["vanilla", "file/loot.zip"], DatapackListParser.Packs(Old));

    [Fact]
    public void Packs_None_Empty() => Assert.Empty(DatapackListParser.Packs("[12:00:01 INFO]: There are no data packs enabled"));

    [Fact]
    public void Lists_FindsFileName()
    {
        Assert.True(DatapackListParser.Lists(Modern, "pack (1).zip"));
        Assert.False(DatapackListParser.Lists(Modern, "pack.zip"));
    }
}
