using System.Text;

namespace WaNut.Core.Tunnels;

/// <summary>
/// The steps around playit.gg's API: waiting while the user approves this PC in the browser, and keeping one tunnel
/// per server that leads to its current port.
/// </summary>
public sealed class PlayitTunnels(PlayitApi api)
{
    public static readonly TimeSpan DefaultPollEvery = TimeSpan.FromSeconds(2);

    /// <summary>playit's limit for a tunnel name.</summary>
    public const int MaxNameLength = 30;

    /// <summary>
    /// Waits until the user approved <paramref name="code"/> on its claim page and returns the agent's secret key.
    /// Throws <see cref="PlayitException"/> "UserRejected" when they refused.
    /// </summary>
    public async Task<string> WaitForApprovalAsync(
        string code, string version, IProgress<PlayitClaimStatus>? progress = null, TimeSpan? pollEvery = null,
        CancellationToken cancellationToken = default)
    {
        var delay = pollEvery ?? DefaultPollEvery;
        while (true)
        {
            var status = await api.ClaimSetupAsync(code, version, cancellationToken).ConfigureAwait(false);
            progress?.Report(status);
            if (status == PlayitClaimStatus.Rejected)
            {
                throw new PlayitException("UserRejected");
            }

            if (status == PlayitClaimStatus.Accepted
                && await api.ClaimExchangeAsync(code, cancellationToken).ConfigureAwait(false) is { } secret)
            {
                return secret;
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The server's tunnel, made when it has none yet (or it was deleted on playit.gg) and pointed at
    /// <paramref name="port"/> when the port changed. Waits a little for a new tunnel's address.
    /// </summary>
    public async Task<PlayitTunnel> EnsureTunnelAsync(
        string secret, Guid? tunnelId, string serverName, int port, TimeSpan? pollEvery = null, CancellationToken cancellationToken = default)
    {
        var tunnels = await api.ListTunnelsAsync(secret, cancellationToken).ConfigureAwait(false);
        var tunnel = tunnels.FirstOrDefault(t => t.Id == tunnelId);
        if (tunnel is null)
        {
            var id = await CreateAsync(secret, TunnelName(serverName), port, pollEvery ?? DefaultPollEvery, cancellationToken).ConfigureAwait(false);
            return await WaitForAddressAsync(secret, id, pollEvery ?? DefaultPollEvery, cancellationToken).ConfigureAwait(false);
        }

        if (tunnel.LocalPort != port)
        {
            await api.SetLocalPortAsync(secret, tunnel.Id, port, cancellationToken).ConfigureAwait(false);
            tunnel = tunnel with { LocalPort = port };
        }

        return tunnel.Address is null
            ? await WaitForAddressAsync(secret, tunnel.Id, pollEvery ?? DefaultPollEvery, cancellationToken).ConfigureAwait(false)
            : tunnel;
    }

    /// <summary>playit accepts short ASCII names; "WaNut: " plus what is left of the server's name.</summary>
    public static string TunnelName(string serverName)
    {
        var ascii = new StringBuilder();
        foreach (var c in serverName.Trim())
        {
            if (c is >= ' ' and <= '~')
            {
                ascii.Append(c);
            }
        }

        var name = ascii.ToString().Trim();
        var full = name.Length == 0 ? "WaNut server" : $"WaNut: {name}";
        return full.Length <= MaxNameLength ? full : full[..MaxNameLength].TrimEnd();
    }

    // The agent was just started; playit accepts the tunnel once the agent has connected, usually within seconds.
    private async Task<Guid> CreateAsync(string secret, string name, int port, TimeSpan pollEvery, CancellationToken cancellationToken)
    {
        var agentId = await api.GetAgentIdAsync(secret, cancellationToken).ConfigureAwait(false);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await api.CreateTunnelAsync(secret, agentId, name, port, cancellationToken).ConfigureAwait(false);
            }
            catch (PlayitException e) when (e.Code == "AgentVersionTooOld" && attempt < 15)
            {
                await Task.Delay(pollEvery, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<PlayitTunnel> WaitForAddressAsync(string secret, Guid id, TimeSpan pollEvery, CancellationToken cancellationToken)
    {
        // playit names a new tunnel within seconds; after that the tunnel is returned as is and the caller shows "not ready".
        PlayitTunnel? latest = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var tunnels = await api.ListTunnelsAsync(secret, cancellationToken).ConfigureAwait(false);
            latest = tunnels.FirstOrDefault(t => t.Id == id);
            if (latest?.Address is not null)
            {
                return latest;
            }

            await Task.Delay(pollEvery, cancellationToken).ConfigureAwait(false);
        }

        return latest ?? throw new PlayitException("TunnelNotFound");
    }
}
