using WaNut.Core.Hotplug.Safety;

namespace WaNut.Core.Tests.Hotplug.Safety;

public sealed class ReloadLogPatternsTests
{
    [Theory]
    [InlineData("[12:00:00] [Worker-Main-2/ERROR]: Couldn't parse element minecraft:loot_table/ev:chest - Not a JSON object")]
    [InlineData("[12:00:00] [Server thread/ERROR]: Couldn't parse data file ev:a from ev:loot_tables/a.json")]
    [InlineData("[12:00:00 ERROR]: Couldn't parse loot table ev:chest")]
    [InlineData("[19Nov2026 12:00:00.000] [Server thread/ERROR] [minecraft/LootDataManager]: Couldn't parse element ev:a")]
    [InlineData("[12:00:00] [Server thread/WARN]: Missing metadata in pack file/broken.zip")]
    [InlineData("[12:00:00] [Server thread/ERROR]: Failed to load function ev:tick")]
    [InlineData("[12:00:00] [Server thread/ERROR]: Unknown registry key in ResourceKey[minecraft:root / minecraft:worldgen/biome]: ev:nope")]
    [InlineData("[12:00:00] [Server thread/ERROR]: Failed to load datapacks, can't proceed with server load")]
    [InlineData("[12:00:00] [Server thread/INFO]: Reload failed; keeping old data")]
    [InlineData("[12:00:00 INFO]: Reload failed; keeping old data")]
    public void IsFailure_LoadError_True(string line) => Assert.True(ReloadLogPatterns.IsFailure(line));

    [Theory]
    [InlineData("[12:00:00] [Server thread/INFO]: Reloading!")]
    [InlineData("[12:00:00] [Server thread/INFO]: <Steve> Couldn't parse data file lol")]
    [InlineData("[12:00:00] [Async Chat Thread - #0/INFO]: <Steve> Reload failed; keeping old data")]
    [InlineData("[12:00:00] [Server thread/INFO]: [Steve] Reload failed; keeping old data")]
    [InlineData("[12:00:00] [Server thread/INFO]: Couldn't parse data file ev:a")]
    [InlineData("[12:00:00] [Server thread/ERROR]: Can't keep up! Is the server overloaded?")]
    [InlineData("Couldn't parse data file ev:a")]
    public void IsFailure_OtherLine_False(string line) => Assert.False(ReloadLogPatterns.IsFailure(line));

    [Theory]
    [InlineData("\tat net.minecraft.Foo.bar(Foo.java:12)", true)]
    [InlineData("com.google.gson.JsonSyntaxException: Expected BEGIN_OBJECT", true)]
    [InlineData("[12:00:00] [Server thread/INFO]: Reloading!", false)]
    public void IsContinuation_Line_TellsLinesWithoutPrefix(string line, bool expected) =>
        Assert.Equal(expected, ReloadLogPatterns.IsContinuation(line));

    [Fact]
    public void Message_PaperLine_StripsPrefix() =>
        Assert.Equal("Reloading!", ReloadLogPatterns.Message("[12:00:00 INFO]: Reloading!"));
}
