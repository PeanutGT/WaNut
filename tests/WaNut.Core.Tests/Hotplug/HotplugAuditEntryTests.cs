using WaNut.Core.Hotplug;

namespace WaNut.Core.Tests.Hotplug;

public sealed class HotplugAuditEntryTests
{
    [Fact]
    public void ToLine_ParseAll_RoundTripsNewestFirst()
    {
        var first = new HotplugAuditEntry(DateTimeOffset.FromUnixTimeSeconds(100), "PC", HotplugState.Completed, null, [new("a.zip", HotplugSwapResult.Added, "ab")]);
        var second = new HotplugAuditEntry(DateTimeOffset.FromUnixTimeSeconds(200), "PC", HotplugState.RolledBack, "broken", []);
        var text = first.ToLine() + "garbage\n" + second.ToLine();

        var parsed = HotplugAuditEntry.ParseAll(text);

        Assert.Equal(2, parsed.Count);
        Assert.Equal((HotplugState.RolledBack, "broken"), (parsed[0].Outcome, parsed[0].Message));
        Assert.Equal("a.zip", parsed[1].Packs[0].Name);
        Assert.Contains("\"outcome\":\"Completed\"", first.ToLine());
        Assert.EndsWith("\n", first.ToLine());
        Assert.Single(first.ToLine().Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
}
