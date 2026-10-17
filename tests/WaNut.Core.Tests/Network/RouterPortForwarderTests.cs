using WaNut.Core.Network;

namespace WaNut.Core.Tests.Network;

/// <summary>
/// Talks to the real home router: forwards a spare port for a minute, then removes it. A router with UPnP and
/// NAT-PMP off answers nothing, which passes too. CI skips this; run it with: dotnet test --filter Category=Network
/// </summary>
[Trait("Category", "Network")]
public sealed class RouterPortForwarderTests
{
    private const int SparePort = 25599;

    [Fact]
    public async Task Open_ThenClose_OnTheHomeRouter()
    {
        var forwarder = new RouterPortForwarder();

        var result = await forwarder.OpenAsync(SparePort, TimeSpan.FromMinutes(1), "WaNut test");
        await forwarder.CloseAsync(SparePort);

        // A router with UPnP off gives NoRouter; one that answers must not refuse a spare port.
        Assert.True(result.Outcome is not (PortForwardOutcome.Refused or PortForwardOutcome.Conflict), $"{result}");
        if (result.Outcome == PortForwardOutcome.Opened)
        {
            Assert.NotNull(result.PublicAddress);
        }
    }
}
