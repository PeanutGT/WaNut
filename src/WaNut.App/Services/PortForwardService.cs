using WaNut.Core.Network;

namespace WaNut.App.Services;

/// <summary>
/// Keeps the running server's port forwarded on the router while "Open the port on my router" is on (blueprint 2),
/// renewing the lease so a rule left behind by a crash or power cut ends by itself.
/// </summary>
public sealed class PortForwardService(IPortForwarder forwarder)
{
    public static readonly TimeSpan Lease = TimeSpan.FromHours(1);

    public static readonly TimeSpan RenewEvery = TimeSpan.FromMinutes(20);

    private const string Description = "WaNut Minecraft server";

    // One server runs at a time, but the one stopping may close after the next one opened: only the owner closes.
    private string? _owner;
    private int _port;
    private CancellationTokenSource? _renewal;

    /// <summary>Forwards <paramref name="port"/> for the server <paramref name="owner"/>, replacing any earlier rule.</summary>
    public async Task<PortForwardResult> OpenAsync(string owner, int port)
    {
        if (_owner is { } previous)
        {
            await CloseAsync(previous);
        }

        var result = await forwarder.OpenAsync(port, Lease, Description);
        if (result.Outcome == PortForwardOutcome.Opened)
        {
            _owner = owner;
            _port = port;
            _renewal = new CancellationTokenSource();
            _ = RenewAsync(port, _renewal.Token);
        }

        return result;
    }

    /// <summary>Removes the rule if <paramref name="owner"/> added it.</summary>
    public async Task CloseAsync(string owner)
    {
        if (_owner != owner)
        {
            return;
        }

        _owner = null;
        _renewal?.Cancel();
        _renewal?.Dispose();
        _renewal = null;
        await forwarder.CloseAsync(_port);
    }

    /// <summary>Before WaNut closes.</summary>
    public Task CloseAnyAsync() => _owner is { } owner ? CloseAsync(owner) : Task.CompletedTask;

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
