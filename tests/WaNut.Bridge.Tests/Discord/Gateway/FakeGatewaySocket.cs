using System.Net.WebSockets;
using System.Threading.Channels;
using WaNut.Bridge.Discord.Gateway;

namespace WaNut.Bridge.Tests.Discord.Gateway;

/// <summary>Plays queued Gateway messages and records what the client sends.</summary>
internal sealed class FakeGatewaySocket : IGatewaySocket
{
    private readonly Channel<string?> _incoming = Channel.CreateUnbounded<string?>();

    public List<string> Sent { get; } = [];

    public int? CloseCode { get; private set; }

    public bool Aborted { get; private set; }

    public TaskCompletionSource FirstSend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Push(string json) => _incoming.Writer.TryWrite(json);

    public void Close(int code)
    {
        CloseCode = code;
        _incoming.Writer.TryWrite(null);
    }

    public Task SendAsync(string text, CancellationToken cancellationToken)
    {
        lock (Sent)
        {
            Sent.Add(text);
        }

        FirstSend.TrySetResult();
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _incoming.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            throw new WebSocketException("aborted");
        }
    }

    public void Abort()
    {
        Aborted = true;
        _incoming.Writer.TryComplete();
    }

    public ValueTask DisposeAsync() => default;
}
