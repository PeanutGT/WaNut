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
                Roster.Add(e.Player!);
                break;
            case GameEventKind.Left:
                Roster.Remove(e.Player!);
                break;
            case GameEventKind.ServerStarted or GameEventKind.ServerStopping:
                Roster.Clear();
                break;
        }

        if (ShouldForward(e))
        {
            post(MessageFormatter.Format(e, config.Presentation));
        }
    }

    public async Task OnDiscordMessageAsync(InboundMessage message, ulong botUserId, CancellationToken cancellationToken)
    {
        var action = InboundRouter.Route(message, config, botUserId);
        switch (action.Kind)
        {
            case InboundActionKind.Chat:
                // Lost while the server is off; a Discord-only chat is fine then.
                await rcon.TryExecuteAsync(Tellraw.Build(action.Author!, action.Text!), cancellationToken).ConfigureAwait(false);
                break;
            case InboundActionKind.ListPlayers:
                var output = await rcon.TryExecuteAsync("list", cancellationToken).ConfigureAwait(false);
                Reply(output is null ? BridgeTexts.ServerOffline
                    : PlayerList.TryParse(output) is { } list ? BridgeTexts.Online(list)
                    : DiscordWebhook.Escape(PlainOutput(output)));
                break;
            case InboundActionKind.Console:
                log.Info($"Remote console: {action.Author} ran \"{action.Text}\".");
                var result = await rcon.TryExecuteAsync(action.Text!, cancellationToken).ConfigureAwait(false);
                Reply(result is null ? BridgeTexts.ServerOffline : CodeBlock(result));
                break;
            case InboundActionKind.ConsoleDenied:
                Reply(BridgeTexts.ConsoleDenied);
                break;
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

    private void Reply(string text) => post(new OutgoingMessage(text));

    private static string CodeBlock(string output)
    {
        var text = PlainOutput(output);
        if (text.Length == 0)
        {
            return BridgeTexts.ConsoleNoOutput;
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
