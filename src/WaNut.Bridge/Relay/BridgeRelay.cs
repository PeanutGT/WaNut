using WaNut.Bridge.Discord;
using WaNut.Bridge.GameLog;
using WaNut.Bridge.Rcon;
using WaNut.Core.Bridge;
using WaNut.Core.Notifications;

namespace WaNut.Bridge.Relay;

/// <summary>The bridge's rules, apart from files, sockets and Discord: what crosses, and how.</summary>
public sealed class BridgeRelay(BridgeConfig config, IRconCommands rcon, Action<OutgoingMessage> post, BridgeLog log)
{
    public const int MaxConsoleOutputLength = 1800;

    public PlayerRoster Roster { get; } = new();

    /// <summary>Handles one log line and returns what it was, or null for any other line.</summary>
    public GameEvent? OnLogLine(string line)
    {
        var e = MinecraftLogParser.Parse(line);
        if (e is not null)
        {
            OnGameEvent(e);
        }

        return e;
    }

    public void OnGameEvent(GameEvent e)
    {
        switch (e.Kind)
        {
            case GameEventKind.Joined:
                if (!Roster.Add(e.Player!))
                {
                    return;
                }
                break;
            case GameEventKind.Left:
                if (!Roster.Remove(e.Player!))
                {
                    return;
                }
                break;
            case GameEventKind.ServerStarted or GameEventKind.ServerStopping:
                Roster.Clear();
                break;
        }

        if (ShouldForward(e))
        {
            // Who and what kind, never the message text: the Discord tab shows this log to the host.
            log.Info($"Minecraft to Discord: {e.Kind}{(e.Player is null ? string.Empty : " " + e.Player)}.");
            post(MessageFormatter.Format(e, config.Presentation, Texts));
        }
    }

    public BridgeTexts Texts { get; } = BridgeTexts.For(config.Language);

    public async Task OnDiscordMessageAsync(InboundMessage message, ulong botUserId, CancellationToken cancellationToken)
    {
        var action = InboundRouter.Route(message, config, botUserId);
        if (action.Kind == InboundActionKind.Chat)
        {
            // Lost while the server is off; a Discord-only chat is fine then.
            log.Info($"Discord to Minecraft: Chat {action.Author}.");
            await rcon.TryExecuteAsync(Tellraw.Build(action.Author!, action.Text!), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Runs a slash command and returns the answer for the person who used it; null when there is nothing to answer.</summary>
    public async Task<string?> RunCommandAsync(InboundAction action, CancellationToken cancellationToken)
    {
        switch (action.Kind)
        {
            case InboundActionKind.ListPlayers:
                var output = await rcon.TryExecuteAsync("list", cancellationToken).ConfigureAwait(false);
                return output is null ? Texts.ServerOffline
                    : PlayerList.TryParse(output) is { } list ? Texts.Online(list)
                    : DiscordWebhook.Escape(PlainOutput(output));
            case InboundActionKind.Console:
                log.Info($"Remote console: {action.Author} ran \"{action.Text}\".");
                var result = await rcon.TryExecuteAsync(action.Text!, cancellationToken).ConfigureAwait(false);
                return result is null ? Texts.ServerOffline : CodeBlock(result);
            case InboundActionKind.ConsoleDenied:
                return Texts.ConsoleDenied;
            case InboundActionKind.WrongChannel:
                return Texts.WrongChannel;
            default:
                return null;
        }
    }

    /// <summary>Asks the server who is online, for the roster and the bot status; null when RCON is down.</summary>
    public async Task<PlayerList?> RefreshPlayersAsync(CancellationToken cancellationToken)
    {
        var output = await rcon.TryExecuteAsync("list", cancellationToken).ConfigureAwait(false);
        var list = output is null ? null : PlayerList.TryParse(output);

        // Plugins can decorate names ("[Admin] Steve"); trust the names only when every one of them was read.
        if (list is not null && list.Names.Count == list.Online)
        {
            Roster.Replace(list.Names);
        }

        return list;
    }

    private bool ShouldForward(GameEvent e) => e.Kind switch
    {
        GameEventKind.Chat => config.Filters.Chat && !InboundRouter.HasIgnoredPrefix(e.Text!, config.Filters),
        GameEventKind.Joined or GameEventKind.Left => config.Filters.JoinLeave,
        GameEventKind.Death or GameEventKind.Advancement => config.Filters.DeathsAndAdvancements && Roster.MayBeOnline(e.Player!),
        _ => config.Filters.ServerState,
    };

    private string CodeBlock(string output)
    {
        var text = PlainOutput(output);
        if (text.Length == 0)
        {
            return Texts.ConsoleNoOutput;
        }

        if (text.Length > MaxConsoleOutputLength)
        {
            text = text[..MaxConsoleOutputLength] + "…";
        }

        // A zero-width space keeps server output from closing the code block early.
        return "```\n" + text.Replace("```", "`​``", StringComparison.Ordinal) + "\n```";
    }

    private static string PlainOutput(string output)
    {
        var chars = new List<char>(output.Length);
        for (var i = 0; i < output.Length; i++)
        {
            if (output[i] == '§')
            {
                i++;
                continue;
            }

            chars.Add(output[i]);
        }

        return new string([.. chars]).Trim();
    }
}
