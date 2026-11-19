using System.Globalization;
using WaNut.Bridge.Discord;
using WaNut.Bridge.Discord.Gateway;
using WaNut.Bridge.GameLog;
using WaNut.Bridge.Rcon;
using WaNut.Bridge.Relay;
using WaNut.Core.Bridge;

namespace WaNut.Bridge;

/// <summary>
/// Runs the bridge until cancelled: follows the log, keeps RCON and the Discord bot connected and keeps the bot
/// status up to date. Each part recovers on its own, so a restarting server or a network blip needs nobody.
/// </summary>
public sealed class BridgeEngine(BridgeConfig config, HttpClient http, BridgeLog log, string? stateDirectory = null)
{
    public const string RconHost = "127.0.0.1";

    /// <summary>How often the server is asked who is online, which also notices a dead RCON connection.</summary>
    public static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(15);

    /// <summary>Discord limits status changes; a burst of joins becomes one change.</summary>
    public static readonly TimeSpan MinPresenceGap = TimeSpan.FromSeconds(5);

    /// <summary>How long after the first crash line the log is read for the cause.</summary>
    public static readonly TimeSpan CrashSettleTime = TimeSpan.FromSeconds(3);

    private RconLink? _rcon;
    private DiscordGateway? _gateway;

    /// <summary>What the status pipe answers: how the connections are right now.</summary>
    public BridgeStatus Snapshot()
    {
        var gateway = _gateway;
        return new BridgeStatus
        {
            State = BridgeState.Running,
            ProcessId = Environment.ProcessId,
            RconConnected = _rcon?.IsConnected == true,
            DiscordConnected = gateway?.IsConnected == true,
            DiscordLatencyMs = gateway is { IsConnected: true, Latency.Ticks: > 0 } ? (int)gateway.Latency.TotalMilliseconds : null,
            ReportedAt = DateTimeOffset.UtcNow,
        };
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var outbox = new DiscordOutbox(CreatePoster(), log);
        var rcon = _rcon = new RconLink(token => RconClient.ConnectAsync(RconHost, config.RconPort, config.RconPassword, token), log);
        var relay = new BridgeRelay(config, rcon, outbox.Enqueue, log);
        // The rest of a crash report and its stack trace arrive over the next seconds; read them before judging.
        relay.CrashSuspected += () =>
        {
            var since = DateTime.UtcNow;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(CrashSettleTime, stop.Token).ConfigureAwait(false);
                    relay.ReportCrash(since - TimeSpan.FromMinutes(1));
                }
                catch (OperationCanceledException)
                {
                    // Stopping.
                }
            });
        };
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
        var gateway = _gateway = string.IsNullOrWhiteSpace(config.BotToken) ? null : new DiscordGateway(config.BotToken, log);
        var gatewayRun = Task.CompletedTask;
        var tasks = new List<Task>
        {
            outbox.RunAsync(stop.Token),
            rcon.RunAsync(stop.Token),
            FollowLogAsync(relay, rcon, Kick, stop.Token),
            StatusLoopAsync(relay, rcon, gateway, statusKick, stop.Token),
        };

        if (gateway is not null)
        {
            var rest = new DiscordRest(http, config.BotToken!);
            var commandsRegistered = false;
            // Handed to a queue: waiting for RCON here would stop the Gateway's read loop from answering heartbeats.
            var chat = new InboundChatQueue((message, token) => relay.OnDiscordMessageAsync(message, gateway.BotUserId, token), log);
            tasks.Add(chat.RunAsync(stop.Token));
            gateway.MessageReceived += message =>
            {
                chat.Enqueue(message);
                return Task.CompletedTask;
            };
            var idleBoard = ulong.TryParse(config.ChannelId, NumberStyles.None, CultureInfo.InvariantCulture, out var channel);
            var board = new StatusBoard(
                rest,
                config.Filters.StatusBoard && idleBoard ? channel : null,
                relay,
                relay.Texts.Chinese,
                Path.Combine(stateDirectory ?? AppContext.BaseDirectory, "status-board.json"),
                log);
            // REST calls and RCON can be slow; off the Gateway's read loop so heartbeats keep being answered.
            gateway.CommandReceived += command =>
            {
                _ = Task.Run(() => AnswerCommandAsync(relay, rest, board, command, stop.Token), stop.Token);
                return Task.CompletedTask;
            };
            gateway.Ready += applicationId =>
            {
                if (!commandsRegistered)
                {
                    commandsRegistered = true;
                    _ = Task.Run(async () => commandsRegistered = await RegisterCommandsAsync(rest, applicationId, stop.Token).ConfigureAwait(false), stop.Token);
                }

                return Task.CompletedTask;
            };

            // Not in the list WhenAny watches: a refused token ends only Discord to game, not the whole bridge.
            gatewayRun = gateway.RunAsync(stop.Token);

            tasks.Add(board.RunAsync(stop.Token));
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
                await Task.WhenAll([.. tasks, gatewayRun]).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stopping.
            }
        }
    }

    private async Task<bool> RegisterCommandsAsync(DiscordRest rest, string applicationId, CancellationToken cancellationToken)
    {
        try
        {
            var guildId = await rest.GetGuildIdAsync(ulong.Parse(config.ChannelId!, CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            await rest.RegisterCommandsAsync(
                applicationId,
                guildId,
                SlashCommandDefinitions.Build(config.Filters.ListCommand, config.Filters.RemoteConsole),
                cancellationToken).ConfigureAwait(false);
            log.Info("Slash commands registered.");
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or KeyNotFoundException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            log.Error($"Could not register slash commands: {ex.Message} Invite the bot with the applications.commands scope.");
            return false;
        }
    }

    private async Task AnswerCommandAsync(BridgeRelay relay, DiscordRest rest, StatusBoard board, SlashCommand command, CancellationToken cancellationToken)
    {
        var action = InboundRouter.Route(command, config);
        if (action.Kind == InboundActionKind.Ignore)
        {
            return;
        }

        try
        {
            // Everyone may see who is online; console output and refusals are for the asker only.
            await rest.DeferReplyAsync(command, ephemeral: action.Kind != InboundActionKind.ListPlayers, cancellationToken).ConfigureAwait(false);
            var answer = action.Kind == InboundActionKind.Status
                ? await PlaceBoardAsync(board, relay, action, cancellationToken).ConfigureAwait(false)
                : await relay.RunCommandAsync(action, cancellationToken).ConfigureAwait(false);
            if (answer is not null)
            {
                await rest.EditReplyAsync(command, answer, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            log.Warn($"Could not answer /{command.Name}: {ex.Message}");
        }
    }

    private async Task<string> PlaceBoardAsync(StatusBoard board, BridgeRelay relay, InboundAction action, CancellationToken cancellationToken)
    {
        try
        {
            var placement = await board.PlaceAsync(action.ChannelId, cancellationToken).ConfigureAwait(false);
            log.Info($"Status board: {action.Author} ({action.UserId}) used /status in channel {action.ChannelId}: {placement}.");
            return relay.Texts.StatusPlaced(placement, action.ChannelId);
        }
        catch (HttpRequestException ex)
        {
            log.Warn($"Status board: could not post in channel {action.ChannelId}: {ex.Message}");
            return relay.Texts.StatusFailed(action.ChannelId);
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
        var lastTrim = Environment.TickCount64;
        while (!cancellationToken.IsCancellationRequested)
        {
            var list = rcon.IsConnected ? await relay.RefreshPlayersAsync(cancellationToken).ConfigureAwait(false) : null;
            var presence = list is not null ? relay.Texts.Presence(list)
                : rcon.IsConnected ? relay.Texts.PresenceOnline
                : relay.Texts.PresenceOffline;

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
            if (Environment.TickCount64 - lastTrim >= StatusInterval.TotalMilliseconds)
            {
                TrimMemory();
                lastTrim = Environment.TickCount64;
            }
        }
    }

    /// <summary>
    /// Posts that wait out a rate limit live longer than a young-generation collection, so under a long flood the
    /// runtime lets the old generation grow for minutes before it looks (about 4 MB to 40 MB in 150 seconds at 40
    /// chat lines a second). A full collection of this small heap takes a few milliseconds, so it is done at the pace
    /// of the status loop, keeping the resident size flat instead of waiting for the runtime's own budget.
    /// </summary>
    private static void TrimMemory() => GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
}
