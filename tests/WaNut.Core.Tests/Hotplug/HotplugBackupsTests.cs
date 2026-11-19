using WaNut.Core.Hotplug;
using WaNut.Core.Storage;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Hotplug;

public sealed class HotplugBackupsTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void NameFor_AndTryParse_RoundTrip()
    {
        var when = new DateTimeOffset(2026, 11, 19, 6, 41, 52, TimeSpan.Zero);

        var name = HotplugBackups.NameFor("loot.v2.zip", when);

        Assert.Equal("loot.v2.20261119T064152Z.zip", name);
        Assert.True(HotplugBackups.TryParse(name, out var pack, out var parsed));
        Assert.Equal("loot.v2.zip", pack);
        Assert.Equal(when, parsed);
    }

    [Theory]
    [InlineData("loot.zip")]
    [InlineData("loot.2026.zip")]
    [InlineData("loot.20261319T064152Z.zip")]
    [InlineData(".20261119T064152Z.zip")]
    public void TryParse_NotABackup_False(string name) => Assert.False(HotplugBackups.TryParse(name, out _, out _));

    [Fact]
    public async Task PruneAsync_KeepsNewestPerPackAndLeavesOthers()
    {
        var folder = Path.Combine(_dir.Path, ".nut-backup", "datapacks");
        Directory.CreateDirectory(folder);
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 7; i++)
        {
            File.WriteAllText(Path.Combine(folder, HotplugBackups.NameFor("a.zip", start.AddHours(i))), "x");
        }

        File.WriteAllText(Path.Combine(folder, HotplugBackups.NameFor("b.zip", start)), "x");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "x");
        await using var files = new LocalFileSystemAdapter(_dir.Path);

        var deleted = await HotplugBackups.PruneAsync(files, ".nut-backup/datapacks", keep: 5);

        Assert.Equal(2, deleted.Count);
        var left = Directory.GetFiles(folder).Select(Path.GetFileName).ToList();
        Assert.Equal(5 + 1 + 1, left.Count);
        Assert.DoesNotContain(HotplugBackups.NameFor("a.zip", start), left);
        Assert.DoesNotContain(HotplugBackups.NameFor("a.zip", start.AddHours(1)), left);
        Assert.Contains(HotplugBackups.NameFor("a.zip", start.AddHours(6)), left);
        Assert.Contains("notes.txt", left);
    }

    [Fact]
    public async Task PruneAsync_NoFolder_ReturnsEmpty()
    {
        await using var files = new LocalFileSystemAdapter(_dir.Path);

        Assert.Empty(await HotplugBackups.PruneAsync(files, ".nut-backup/datapacks"));
    }
}
