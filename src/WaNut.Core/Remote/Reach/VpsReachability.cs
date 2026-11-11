using System.Net.Sockets;

namespace WaNut.Core.Remote.Reach;

/// <summary>
/// Checks from this PC whether friends can reach a server on the VPS. This PC is outside the VPS's network, so one
/// TCP connection to host:port goes through the same firewalls a friend's connection would.
/// </summary>
public static class VpsReachability
{
    /// <summary>A dropped connection never answers, so this is how long to wait before calling it <see cref="VpsReach.NoAnswer"/>.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public static async Task<VpsReach> CheckAsync(string host, int port, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(timeout ?? Timeout);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host.Trim().Trim('[', ']'), port, wait.Token).ConfigureAwait(false);
            return VpsReach.Open;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return VpsReach.NoAnswer;
        }
        catch (SocketException e)
        {
            return Classify(e.SocketErrorCode);
        }
    }

    public static VpsReach Classify(SocketError error) => error switch
    {
        SocketError.ConnectionRefused or SocketError.ConnectionReset => VpsReach.Refused,
        SocketError.TimedOut => VpsReach.NoAnswer,

        // iptables "reject-with icmp-host-prohibited" (Oracle's default) arrives as unreachable on Linux clients.
        SocketError.HostUnreachable => VpsReach.Refused,
        _ => VpsReach.Unknown,
    };
}
