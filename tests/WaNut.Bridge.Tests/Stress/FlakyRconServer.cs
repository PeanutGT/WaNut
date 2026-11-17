using System.Net;
using System.Net.Sockets;
using WaNut.Bridge.Rcon;

namespace WaNut.Bridge.Tests.Stress;

/// <summary>An RCON server on a fixed loopback port that can be stopped and started again, the way a restarting Minecraft server is.</summary>
internal sealed class FlakyRconServer(string password) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<TcpClient> _clients = [];
    private TcpListener? _listener;
    private int _port;

    public int Port => _port;

    public List<string> Commands { get; } = [];

    public int Connections { get; private set; }

    /// <summary>Accept and sign in, then never answer a command: a server that is frozen.</summary>
    public bool Hang { get; set; }

    public void Start()
    {
        lock (_gate)
        {
            var listener = new TcpListener(IPAddress.Loopback, _port);
            listener.Start();
            _port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _listener = listener;
            _ = AcceptLoopAsync(listener);
        }
    }

    /// <summary>The server process dies: the port closes and every connection breaks.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _listener?.Stop();
            _listener = null;
            foreach (var client in _clients)
            {
                client.Dispose();
            }

            _clients.Clear();
        }
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return default;
    }

    private async Task AcceptLoopAsync(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync();
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            lock (_gate)
            {
                if (_listener != listener)
                {
                    // Accepted just as the server was stopped.
                    client.Dispose();
                    return;
                }

                Connections++;
                _clients.Add(client);
            }

            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            while (true)
            {
                var request = await RconPacket.ReadAsync(stream, CancellationToken.None);
                if (request.Type == RconPacket.TypeLogin)
                {
                    var ok = request.Body == password;
                    await stream.WriteAsync(new RconPacket(ok ? request.RequestId : -1, RconPacket.TypeLoginResponse, string.Empty).Encode());
                    continue;
                }

                lock (Commands)
                {
                    Commands.Add(request.Body);
                }

                if (Hang)
                {
                    continue;
                }

                await stream.WriteAsync(new RconPacket(request.RequestId, RconPacket.TypeResponse, "ok").Encode());
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or EndOfStreamException or InvalidOperationException)
        {
            // The client went away or the server was stopped.
        }
    }
}
