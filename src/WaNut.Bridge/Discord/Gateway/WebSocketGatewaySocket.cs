using System.Net.WebSockets;
using System.Text;

namespace WaNut.Bridge.Discord.Gateway;

internal sealed class WebSocketGatewaySocket : IGatewaySocket
{
    /// <summary>Far above any event the bridge's intents bring, so a broken stream cannot grow memory without end.</summary>
    public const int MaxMessageBytes = 4 * 1024 * 1024;

    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly byte[] _buffer = new byte[16 * 1024];

    public int? CloseCode => (int?)_socket.CloseStatus;

    public static async Task<IGatewaySocket> ConnectAsync(Uri url, CancellationToken cancellationToken)
    {
        var socket = new WebSocketGatewaySocket();
        try
        {
            await socket._socket.ConnectAsync(url, cancellationToken).ConfigureAwait(false);
            return socket;
        }
        catch
        {
            await socket.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task SendAsync(string text, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        while (true)
        {
            var result = await _socket.ReceiveAsync(_buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            message.Write(_buffer, 0, result.Count);
            if (message.Length > MaxMessageBytes)
            {
                throw new WebSocketException("A Gateway message was too large.");
            }

            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }
        }
    }

    public void Abort() => _socket.Abort();

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        _sendLock.Dispose();
        return default;
    }
}
