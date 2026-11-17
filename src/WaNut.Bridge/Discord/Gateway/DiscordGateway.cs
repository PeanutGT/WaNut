using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using WaNut.Bridge.Rcon;

namespace WaNut.Bridge.Discord.Gateway;

/// <summary>
/// The bot's live connection to Discord: messages typed in the channel, slash commands and the bot status. Speaks
/// the Gateway protocol directly (Hello, Identify, heartbeats, Resume) over one WebSocket. A dropped connection is
/// resumed so no event is lost; a heartbeat Discord does not answer counts as a dead connection. Only the guild
/// message intents are asked for, so Discord sends no member or presence data and memory stays small.
/// </summary>
public sealed class DiscordGateway(
    string botToken,
    BridgeLog log,
    Func<Uri, CancellationToken, Task<IGatewaySocket>>? connect = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public static readonly Uri DefaultUrl = new("wss://gateway.discord.gg/?v=10&encoding=json");

    private readonly Func<Uri, CancellationToken, Task<IGatewaySocket>> _connect = connect ?? WebSocketGatewaySocket.ConnectAsync;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly Backoff _backoff = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
    private IGatewaySocket? _socket;
    private string? _sessionId;
    private Uri? _resumeUrl;
    private int? _sequence;
    private string? _presence;

    public event Func<InboundMessage, Task>? MessageReceived;

    public event Func<SlashCommand, Task>? CommandReceived;

    /// <summary>A new session is ready; carries the bot's application id, needed to register slash commands.</summary>
    public event Func<string, Task>? Ready;

    /// <summary>The bot's own user id, known once connected; its messages are never relayed back.</summary>
    public ulong BotUserId { get; private set; }

    public bool IsConnected { get; private set; }

    /// <summary>The time Discord took to answer the last heartbeat.</summary>
    public TimeSpan Latency { get; private set; }

    /// <summary>Stays connected until cancelled, or until Discord refuses the token or intents.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            int? closeCode = null;
            try
            {
                await using var socket = await _connect(_resumeUrl ?? DefaultUrl, cancellationToken).ConfigureAwait(false);
                _socket = socket;
                closeCode = await RunSessionAsync(socket, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A network drop, a garbled message or anything else unforeseen: reconnect below. Letting it
                // escape would end this task for good and leave Discord to game chat dead until the bridge restarts.
                // Only the type is logged, never the message, which could quote what Discord sent.
                if (ex is not (WebSocketException or IOException or OperationCanceledException))
                {
                    log.Warn($"Discord connection problem ({ex.GetType().Name}); reconnecting.");
                }
            }
            finally
            {
                _socket = null;
                if (IsConnected)
                {
                    IsConnected = false;
                    log.Warn("Discord connection lost; reconnecting.");
                }
            }

            if (GatewayCloseCodes.IsFatal(closeCode))
            {
                log.Error(GatewayCloseCodes.Explain(closeCode));
                return;
            }

            if (GatewayCloseCodes.EndsSession(closeCode))
            {
                ForgetSession();
            }

            try
            {
                await _delay(_backoff.Next(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Shows "Playing &lt;text&gt;" under the bot's name; kept for later connections too.</summary>
    public async Task UpdatePresenceAsync(string text, CancellationToken cancellationToken)
    {
        _presence = text;
        if (IsConnected && _socket is { } socket)
        {
            await socket.SendAsync(GatewayPayloads.Presence(text), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <returns>The close code when Discord closed the connection, else null.</returns>
    private async Task<int?> RunSessionAsync(IGatewaySocket socket, CancellationToken cancellationToken)
    {
        var hello = await ReceiveFrameAsync(socket, cancellationToken).ConfigureAwait(false);
        if (hello is null)
        {
            return socket.CloseCode;
        }

        if (hello.Op != GatewayOpcode.Hello)
        {
            throw new InvalidOperationException("The Gateway did not start with Hello.");
        }

        var interval = TimeSpan.FromMilliseconds(hello.Data.GetProperty("heartbeat_interval").GetDouble());
        await socket.SendAsync(
            _sessionId is { } session ? GatewayPayloads.Resume(botToken, session, _sequence) : GatewayPayloads.Identify(botToken, _presence),
            cancellationToken).ConfigureAwait(false);

        using var sessionEnd = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = new Heartbeat();
        var beating = HeartbeatAsync(socket, interval, heartbeat, sessionEnd.Token);
        try
        {
            while (true)
            {
                var frame = await ReceiveFrameAsync(socket, sessionEnd.Token).ConfigureAwait(false);
                if (frame is null)
                {
                    return socket.CloseCode;
                }

                if (frame.Sequence is { } sequence)
                {
                    _sequence = sequence;
                }

                switch (frame.Op)
                {
                    case GatewayOpcode.Dispatch:
                        await DispatchAsync(frame).ConfigureAwait(false);
                        break;
                    case GatewayOpcode.Heartbeat:
                        await socket.SendAsync(GatewayPayloads.Heartbeat(_sequence), cancellationToken).ConfigureAwait(false);
                        break;
                    case GatewayOpcode.HeartbeatAck:
                        heartbeat.Acked = true;
                        Latency = heartbeat.Clock.Elapsed;
                        break;
                    case GatewayOpcode.Reconnect:
                        return null;
                    case GatewayOpcode.InvalidSession:
                        if (frame.Data.ValueKind != JsonValueKind.True)
                        {
                            ForgetSession();
                        }

                        return null;
                }
            }
        }
        finally
        {
            await sessionEnd.CancelAsync().ConfigureAwait(false);
            try
            {
                await beating.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
                // The session is over either way.
            }
        }
    }

    private async Task HeartbeatAsync(IGatewaySocket socket, TimeSpan interval, Heartbeat heartbeat, CancellationToken cancellationToken)
    {
        // Discord asks for a random first wait so that reconnecting clients do not all beat at once.
        var wait = interval * Random.Shared.NextDouble();
        while (true)
        {
            await _delay(wait, cancellationToken).ConfigureAwait(false);
            wait = interval;
            if (!heartbeat.Acked)
            {
                // No answer to the last beat: the connection is a zombie. Drop it and resume on a new one.
                socket.Abort();
                return;
            }

            heartbeat.Acked = false;
            heartbeat.Clock.Restart();
            await socket.SendAsync(GatewayPayloads.Heartbeat(_sequence), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchAsync(GatewayFrame frame)
    {
        switch (frame.EventName)
        {
            case "READY":
                _sessionId = GatewayEvents.String(frame.Data, "session_id");
                _resumeUrl = GatewayEvents.String(frame.Data, "resume_gateway_url") is { } url ? new Uri(url + "/?v=10&encoding=json") : null;
                BotUserId = GatewayEvents.Snowflake(frame.Data.GetProperty("user"), "id");
                var applicationId = frame.Data.TryGetProperty("application", out var application) ? GatewayEvents.String(application, "id") : null;
                Connected();
                if (applicationId is not null)
                {
                    await RaiseAsync(Ready, applicationId).ConfigureAwait(false);
                }

                break;
            case "RESUMED":
                Connected();
                break;
            case "MESSAGE_CREATE" when GatewayEvents.Message(frame.Data) is { } message:
                await RaiseAsync(MessageReceived, message).ConfigureAwait(false);
                break;
            case "INTERACTION_CREATE" when GatewayEvents.Command(frame.Data) is { } command:
                await RaiseAsync(CommandReceived, command).ConfigureAwait(false);
                break;
        }

    }

    private void Connected()
    {
        IsConnected = true;
        _backoff.Reset();
        log.Info("Discord connected.");
    }

    private async Task RaiseAsync<T>(Func<T, Task>? handler, T value)
    {
        if (handler is null)
        {
            return;
        }

        try
        {
            await handler(value).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Error($"Could not handle a Discord event: {ex.Message}");
        }
    }

    private void ForgetSession()
    {
        _sessionId = null;
        _resumeUrl = null;
        _sequence = null;
    }

    private static async Task<GatewayFrame?> ReceiveFrameAsync(IGatewaySocket socket, CancellationToken cancellationToken)
    {
        var text = await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        return text is null ? null : GatewayFrame.Parse(text);
    }

    private sealed class Heartbeat
    {
        public volatile bool Acked = true;

        public Stopwatch Clock { get; } = new();
    }
}
