using System.Threading.Channels;
using WaNut.Bridge.Discord;

namespace WaNut.Bridge.Relay;

/// <summary>
/// Hands Discord chat to the game one message at a time, in order, away from the Gateway's read loop. RCON can take
/// ten seconds to give up on a frozen server; if the read loop waited for that, Discord's heartbeat answers would sit
/// unread and the connection would be dropped as dead. When the game cannot keep up the oldest messages are dropped.
/// </summary>
public sealed class InboundChatQueue
{
    public const int Capacity = 200;

    private readonly Func<InboundMessage, CancellationToken, Task> _handle;
    private readonly BridgeLog _log;
    private readonly Channel<InboundMessage> _queue;
    private int _dropped;

    public InboundChatQueue(Func<InboundMessage, CancellationToken, Task> handle, BridgeLog log)
    {
        _handle = handle;
        _log = log;
        _queue = Channel.CreateBounded<InboundMessage>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            _ => OnDropped());
    }

    public void Enqueue(InboundMessage message) => _queue.Writer.TryWrite(message);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await _handle(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warn($"Could not pass a Discord message to the game ({ex.GetType().Name}).");
            }
        }
    }

    private void OnDropped()
    {
        var dropped = Interlocked.Increment(ref _dropped);
        if (dropped == 1 || dropped % 100 == 0)
        {
            _log.Warn($"The game is not keeping up with Discord chat; old messages are being dropped ({dropped} so far).");
        }
    }
}
