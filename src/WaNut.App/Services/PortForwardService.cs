using WaNut.Core.Network;

namespace WaNut.App.Services;

/// <summary>
/// Keeps each running server's port forwarded on the router while "Open the port on my router" is on (blueprint 2),
/// renewing the lease so a rule left behind by a crash or power cut ends by itself.
/// </summary>
public sealed class PortForwardService(IPortForwarder forwarder)
{
    public static readonly TimeSpan Lease = TimeSpan.FromHours(1);

    public static readonly TimeSpan RenewEvery = TimeSpan.FromMinutes(20);

    private const string Description = "WaNut Minecraft server";

    // One rule per running server; each is removed only by the server that added it.
    private readonly Dictionary<string, (int Port, CancellationTokenSource Renewal)> _rules = [];

    /// <summary>Forwards <paramref name="port"/> for the server <paramref name="owner"/>, replacing its earlier rule.</summary>
    public async Task<PortForwardResult> OpenAsync(string owner, int port)
    {
        await CloseAsync(owner);
        var result = await forwarder.OpenAsync(port, Lease, Description);
        if (result.Outcome == PortForwardOutcome.Opened)
        {
            var renewal = new CancellationTokenSource();
            _rules[owner] = (port, renewal);
            _ = RenewAsync(port, renewal.Token);
        }

        return result;
    }

    /// <summary>Removes the rule if <paramref name="owner"/> added it.</summary>
    public async Task CloseAsync(string owner)
    {
        if (!_rules.Remove(owner, out var rule))
        {
            return;
        }

        rule.Renewal.Cancel();
        rule.Renewal.Dispose();
        await forwarder.CloseAsync(rule.Port);
    }

    /// <summary>Before WaNut closes.</summary>
    public Task CloseAnyAsync() => Task.WhenAll(_rules.Keys.ToList().Select(CloseAsync));

    private async Task RenewAsync(int port, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RenewEvery);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                // A failed renewal is tried again next tick; the rule still has most of its lease.
                await forwarder.OpenAsync(port, Lease, Description, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Closed.
        }
    }
}
