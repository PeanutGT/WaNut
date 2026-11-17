using System.Net.Sockets;
using System.Text;

namespace WaNut.Bridge.Rcon;

/// <summary>One signed-in RCON connection. Commands are sent one at a time, as Minecraft answers them in order.</summary>
public sealed class RconClient : IAsyncDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile bool _disposed;
    private int _nextId;

    private RconClient(TcpClient tcp)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
    }

    /// <exception cref="RconAuthenticationException">The password is wrong.</exception>
    /// <exception cref="SocketException">Nothing listens on the port (the server is off or RCON is not on yet).</exception>
    public static async Task<RconClient> ConnectAsync(string host, int port, string password, CancellationToken cancellationToken)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            await tcp.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            var client = new RconClient(tcp);
            await client.LoginAsync(password, timeout.Token).ConfigureAwait(false);
            return client;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    /// <exception cref="ArgumentException">The command is longer than Minecraft accepts.</exception>
    /// <exception cref="IOException">The connection broke; this client is no longer usable.</exception>
    public async Task<string> ExecuteAsync(string command, CancellationToken cancellationToken)
    {
        if (Encoding.UTF8.GetByteCount(command) > RconPacket.MaxRequestBodyBytes)
        {
            throw new ArgumentException("The command is too long for RCON.", nameof(command));
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Disposed while this command waited for its turn.
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            var id = NextId();
            await SendAsync(new RconPacket(id, RconPacket.TypeCommand, command), timeout.Token).ConfigureAwait(false);
            while (true)
            {
                var reply = await ReadAsync(timeout.Token).ConfigureAwait(false);
                if (reply.RequestId == id)
                {
                    return reply.Body;
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _stream.DisposeAsync().ConfigureAwait(false);
        _tcp.Dispose();

        // The lock is not disposed: commands still waiting for it would never be woken, and each one finds
        // the client closed as soon as it gets its turn.
    }

    private async Task LoginAsync(string password, CancellationToken cancellationToken)
    {
        var id = NextId();
        await SendAsync(new RconPacket(id, RconPacket.TypeLogin, password), cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var reply = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (reply.RequestId == -1)
            {
                throw new RconAuthenticationException();
            }

            // Some servers send an empty response packet before the login answer.
            if (reply.Type == RconPacket.TypeLoginResponse && reply.RequestId == id)
            {
                return;
            }
        }
    }

    private int NextId() => _nextId = _nextId == int.MaxValue ? 1 : _nextId + 1;

    private async Task SendAsync(RconPacket packet, CancellationToken cancellationToken)
    {
        await _stream.WriteAsync(packet.Encode(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<RconPacket> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await RconPacket.ReadAsync(_stream, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException ex)
        {
            throw new IOException("The server closed the RCON connection.", ex);
        }
    }
}
