using WaNut.Core.Bridge;

namespace WaNut.Core.Tests.Bridge;

public sealed class BridgeStatusPipeTests
{
    [Fact]
    public void PipeName_OddCharacters_BecomeUnderscores()
    {
        Assert.Equal("WaNut.Bridge.my_server-1", BridgeStatusPipe.PipeName("my server-1"));
    }

    [Fact]
    public void Json_RoundTrip_KeepsTheStatus()
    {
        var status = new BridgeStatus { State = BridgeState.Running, ProcessId = 42, RconConnected = true, DiscordLatencyMs = 80 };

        var json = BridgeStatusPipe.Serialize(status);

        Assert.Contains("\"state\":\"running\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', json);
        Assert.Equal(status, BridgeStatusPipe.Deserialize(json));
    }

    [Fact]
    public async Task AskAsync_NoBridge_ReturnsNull()
    {
        Assert.Null(await BridgeStatusPipe.AskAsync("nobody-" + Guid.NewGuid().ToString("N"), timeout: TimeSpan.FromMilliseconds(300)));
    }
}
