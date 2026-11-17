namespace WaNut.Core.Bridge;

/// <summary>Which things cross between Minecraft and Discord. Setters, not init, for the reason on <see cref="BridgeConfig"/>.</summary>
public sealed record BridgeFilters
{
    /// <summary>Chat both ways. Private messages (/tell, /msg) never cross.</summary>
    public bool Chat { get; set; } = true;

    public bool JoinLeave { get; set; } = true;

    /// <summary>Deaths and advancements.</summary>
    public bool DeathsAndAdvancements { get; set; } = true;

    /// <summary>Server started / stopped notices.</summary>
    public bool ServerState { get; set; } = true;

    /// <summary>Shows "5 / 20 online" in the bot's Discord status.</summary>
    public bool OnlineCountStatus { get; set; } = true;

    /// <summary>Lets Discord users ask who is online.</summary>
    public bool ListCommand { get; set; } = true;

    /// <summary>Lets members of <see cref="BridgeConfig.AdminRoleId"/> run server commands from Discord. Off by default.</summary>
    public bool RemoteConsole { get; set; }

    /// <summary>Messages starting with one of these (for example "!" or "#") are not forwarded either way.</summary>
    public IReadOnlyList<string> IgnoredPrefixes { get; set; } = [];
}
