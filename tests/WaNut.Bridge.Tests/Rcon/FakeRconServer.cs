using System.Net;
using System.Net.Sockets;
using WaNut.Bridge.Rcon;

namespace WaNut.Bridge.Tests.Rcon;

/// <summary>A one-connection RCON server on a free loopback port that answers like Minecraft.</summary>
internal sealed class FakeRconServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Task _serve;

    public FakeRconServer(string password, Func<string, string> answer, bool sendStrayPacketFirst = false)
    {
        _listener.Start();
        _serve = ServeAsync(password, answer, sendStrayPacketFirst);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public List<string> Commands { get; } = [];

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        try
        {
            await _serve;
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Closed by the test.
        }
    }

    private async Task ServeAsync(string password, Func<string, string> answer, bool sendStrayPacketFirst)
    {
        using var client = await _listener.AcceptTcpClientAsync();
        var stream = client.GetStream();
        while (true)
        {
            RconPacket request;
            try
            {
                request = await RconPacket.ReadAsync(stream, CancellationToken.None);
            }
            catch (EndOfStreamException)
            {
                return;
            }

            if (request.Type == RconPacket.TypeLogin)
            {
                var ok = request.Body == password;
                await stream.WriteAsync(new RconPacket(ok ? request.RequestId : -1, RconPacket.TypeLoginResponse, string.Empty).Encode());
                continue;
            }

            Commands.Add(request.Body);
            if (sendStrayPacketFirst)
            {
                await stream.WriteAsync(new RconPacket(request.RequestId + 1000, RconPacket.TypeResponse, "stray").Encode());
            }

            await stream.WriteAsync(new RconPacket(request.RequestId, RconPacket.TypeResponse, answer(request.Body)).Encode());
        }
    }
}
