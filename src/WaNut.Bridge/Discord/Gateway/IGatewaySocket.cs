namespace WaNut.Bridge.Discord.Gateway;

/// <summary>One WebSocket connection to the Gateway, carrying whole text messages.</summary>
public interface IGatewaySocket : IAsyncDisposable
{
    /// <summary>Safe to call from several tasks at once.</summary>
    Task SendAsync(string text, CancellationToken cancellationToken);

    /// <summary>The next message, or null once the connection is closed.</summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>The close code Discord sent, once closed.</summary>
    int? CloseCode { get; }

    /// <summary>Drops the connection at once, without a close frame, so the session can still be resumed.</summary>
    void Abort();
}
