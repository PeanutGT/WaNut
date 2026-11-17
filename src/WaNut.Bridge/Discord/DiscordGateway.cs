using NetCord;
using NetCord.Gateway;

namespace WaNut.Bridge.Discord;

/// <summary>
/// The bot's live connection to Discord, for messages typed in the channel and the "5 / 20 online" status. NetCord
/// reconnects and resumes by itself. Only the guild message intents are asked for, so no guild, member or presence
/// data is cached and memory stays small. Message Content is a privileged intent the bot owner must switch on in
/// the Discord developer portal.
/// </summary>
public sealed class DiscordGateway : IAsyncDisposable
{
    private readonly GatewayClient _client;
    private readonly BridgeLog _log;
    private string? _presence;

    public DiscordGateway(string botToken, BridgeLog log)
    {
        _log = log;
        _client = new GatewayClient(
            new BotToken(botToken),
            new GatewayClientConfiguration { Intents = GatewayIntents.GuildMessages | GatewayIntents.MessageContent });
        _client.Ready += OnReadyAsync;
        _client.Disconnect += OnDisconnectAsync;
        _client.MessageCreate += OnMessageAsync;
    }

    public event Func<InboundMessage, Task>? MessageReceived;

    /// <summary>The bot's own user id, known once connected; its messages are never relayed back.</summary>
    public ulong BotUserId { get; private set; }

    public bool IsConnected { get; private set; }

    public TimeSpan Latency => _client.Latency;

    public Task StartAsync(CancellationToken cancellationToken) => _client.StartAsync(cancellationToken: cancellationToken).AsTask();

    public async Task UpdatePresenceAsync(string text, CancellationToken cancellationToken)
    {
        await _client.UpdatePresenceAsync(Presence(text), cancellationToken: cancellationToken).ConfigureAwait(false);
        _presence = text;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _client.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.WebSockets.WebSocketException)
        {
            // Never connected or already gone.
        }

        _client.Dispose();
    }

    private async ValueTask OnReadyAsync(ReadyEventArgs args)
    {
        BotUserId = args.User.Id;
        IsConnected = true;
        _log.Info("Discord connected.");

        // A new session starts with no status; put the last one back.
        if (_presence is { } presence)
        {
            try
            {
                await _client.UpdatePresenceAsync(Presence(presence)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warn($"Could not restore the Discord status: {ex.Message}");
            }
        }
    }

    private static PresenceProperties Presence(string text) =>
        new PresenceProperties(UserStatusType.Online).WithActivities([new UserActivityProperties(text, UserActivityType.Playing)]);

    private ValueTask OnDisconnectAsync(DisconnectEventArgs args)
    {
        IsConnected = false;
        _log.Warn(args.Reconnect ? "Discord connection lost; reconnecting." : "Discord connection closed.");
        return default;
    }

    private async ValueTask OnMessageAsync(Message message)
    {
        var handler = MessageReceived;
        if (handler is null || message.Author is null)
        {
            return;
        }

        try
        {
            await handler(ToInbound(message)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error($"Could not relay a Discord message: {ex.Message}");
        }
    }

    private static InboundMessage ToInbound(Message message)
    {
        var author = message.Author;
        var member = author as PartialGuildUser;
        var names = new Dictionary<ulong, string>();
        foreach (var user in message.MentionedUsers)
        {
            names[user.Id] = DisplayName(user);
        }

        var content = DiscordText.ToPlain(message.Content ?? string.Empty, names);
        if (message.Attachments.Count > 0)
        {
            content = (content + " [attachment]").Trim();
        }

        return new InboundMessage(
            message.ChannelId,
            author.Id,
            DisplayName(author),
            author.IsBot,
            message.WebhookId is not null,
            member?.RoleIds ?? [],
            content);
    }

    private static string DisplayName(User user) =>
        (user as PartialGuildUser)?.Nickname is { Length: > 0 } nickname ? nickname
        : user.GlobalName is { Length: > 0 } globalName ? globalName
        : user.Username;
}
