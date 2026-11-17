namespace WaNut.Core.Bridge;

/// <summary>Which things cross between Minecraft and Discord.</summary>
public sealed record BridgeFilters
{
    /// <summary>Chat both ways. Private messages (/tell, /msg) never cross.</summary>
    public bool Chat { get; init; } = true;

    public bool JoinLeave { get; init; } = true;

    /// <summary>Deaths and advancements.</summary>
    public bool DeathsAndAdvancements { get; init; } = true;

    /// <summary>Server started / stopped notices.</summary>
    public bool ServerState { get; init; } = true;

    /// <summary>Shows "5 / 20 online" in the bot's Discord status.</summary>
    public bool OnlineCountStatus { get; init; } = true;

    /// <summary>Lets Discord users ask who is online.</summary>
    public bool ListCommand { get; init; } = true;

    /// <summary>Lets members of <see cref="BridgeConfig.AdminRoleId"/> run server commands from Discord. Off by default.</summary>
    public bool RemoteConsole { get; init; }

    /// <summary>Messages starting with one of these (for example "!" or "#") are not forwarded either way.</summary>
    public IReadOnlyList<string> IgnoredPrefixes { get; init; } = [];
}
