using WaNut.Core.Bridge;
using WaNut.Core.Bridge.Local;

namespace WaNut.Core.Tests.Bridge.Local;

public sealed class LocalBridgeRunTests
{
    [Fact]
    public void Parse_AfterToText_GivesTheSameRun()
    {
        var run = new LocalBridgeRun(BridgeRunMode.DetachedProcess, @"C:\Users\A B\WaNut.Bridge.exe", 1234);

        Assert.Equal(run, LocalBridgeRun.Parse(run.ToText()));
    }

    [Fact]
    public void Parse_TaskWithoutPid_HasNoPid()
    {
        Assert.Equal(new LocalBridgeRun(BridgeRunMode.ScheduledTask, "x.exe"), LocalBridgeRun.Parse("ScheduledTask - x.exe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Systemd 1 x")]
    [InlineData("nonsense")]
    public void Parse_Damaged_ReturnsNull(string text)
    {
        Assert.Null(LocalBridgeRun.Parse(text));
    }
}
