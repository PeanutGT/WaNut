using System.Globalization;
using WaNut.Bridge.Discord;
using WaNut.Bridge.Logs;
using WaNut.Bridge.Rcon;
using WaNut.Bridge.Relay;
using WaNut.Core.Bridge;

namespace WaNut.Bridge;

/// <summary>
/// Runs the bridge until cancelled: follows the log, keeps RCON and the Discord bot connected and keeps the bot
/// status up to date. Each part recovers on its own, so a restarting server or a network blip needs nobody.
/// </summary>
public sealed class BridgeEngine(BridgeConfig config, HttpClient http, BridgeLog log)
{
    public const string RconHost = "127.0.0.1";

    /// <summary>How often the server is asked who is online, which also notices a dead RCON connection.</summary>
    public static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(15);

    /// <summary>Discord limits status changes; a burst of joins becomes one change.</summary>
    public static readonly TimeSpan MinPresenceGap = TimeSpan.FromSeconds(5);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var outbox = new DiscordOutbox(CreatePoster(), log);
        var rcon = new RconLink(token => RconClient.ConnectAsync(RconHost, config.RconPort, config.RconPassword, token), log);
        var relay = new BridgeRelay(config, rcon, outbox.Enqueue, log);
        using var statusKick = new SemaphoreSlim(0, 1);
        void Kick()
        {
            if (statusKick.CurrentCount == 0)
            {
                try
                {
                    statusKick.Release();
                }
                catch (SemaphoreFullException)
                {
                    // Already kicked.
                }
            }
        }

        rcon.ConnectionChanged += _ => Kick();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var gateway = string.IsNullOrWhiteSpace(config.BotToken) ? null : new DiscordGateway(config.BotToken, log);
        var tasks = new List<Task>
        {
            outbox.RunAsync(stop.Token),
            rcon.RunAsync(stop.Token),
            FollowLogAsync(relay, rcon, Kick, stop.Token),
            StatusLoopAsync(relay, rcon, gateway, statusKick, stop.Token),
        };

        if (gateway is not null)
        {
            gateway.MessageReceived += message => relay.OnDiscordMessageAsync(message, gateway.BotUserId, stop.Token);
            try
            {
                await gateway.StartAsync(stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Game to Discord still works through the webhook; only Discord to game is lost.
                log.Error($"Could not sign in to Discord with the bot token: {ex.Message}");
            }
        }
        else
        {
            log.Info("No bot token: Minecraft to Discord only.");
        }

        log.Info($"Following {config.LogFile}.");
        try
        {
            await Task.WhenAny(tasks).ConfigureAwait(false);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stopping.
            }
        }
    }

    private IDiscordPoster CreatePoster() =>
        config.Presentation == DiscordPresentation.Webhook
            ? DiscordHttpPoster.ForWebhook(http, config.WebhookUrl!)
            : DiscordHttpPoster.ForChannel(http, config.BotToken!, ulong.Parse(config.ChannelId!, CultureInfo.InvariantCulture));

    private async Task FollowLogAsync(BridgeRelay relay, RconLink rcon, Action kick, CancellationToken cancellationToken)
    {
        var reader = new LogTailReader(config.LogFile);
        await foreach (var line in reader.ReadLinesAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            switch (relay.OnLogLine(line)?.Kind)
            {
                case GameEventKind.ServerStarted:
                    // RCON opens with the server; connect now rather than after the backoff wait.
                    rcon.Wake();
                    kick();
                    break;
                case GameEventKind.Joined or GameEventKind.Left or GameEventKind.ServerStopping:
                    kick();
                    break;
            }
        }
    }

    private async Task StatusLoopAsync(BridgeRelay relay, RconLink rcon, DiscordGateway? gateway, SemaphoreSlim kick, CancellationToken cancellationToken)
    {
        string? shown = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var list = rcon.IsConnected ? await relay.RefreshPlayersAsync(cancellationToken).ConfigureAwait(false) : null;
            var presence = list is not null ? BridgeTexts.Presence(list)
                : rcon.IsConnected ? BridgeTexts.PresenceOnline
                : BridgeTexts.PresenceOffline;

            if (gateway is { IsConnected: true } && config.Filters.OnlineCountStatus && presence != shown)
            {
                try
                {
                    await gateway.UpdatePresenceAsync(presence, cancellationToken).ConfigureAwait(false);
                    shown = presence;
                    await Task.Delay(MinPresenceGap, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.Warn($"Could not update the Discord status: {ex.Message}");
                }
            }

            await kick.WaitAsync(StatusInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
