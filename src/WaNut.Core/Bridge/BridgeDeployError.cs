namespace WaNut.Core.Bridge;

/// <summary>Why a deployment stopped; the app turns each into its own message.</summary>
public enum BridgeDeployError
{
    /// <summary>The request itself is incomplete (no server id, a VPS target without a VPS, a config the bridge would refuse).</summary>
    InvalidRequest,

    /// <summary>Discord answered 401 to the bot token.</summary>
    BotTokenRejected,

    /// <summary>No channel has that id, or the bot is not in its server.</summary>
    ChannelNotFound,

    /// <summary>The bot can't see the channel.</summary>
    ChannelNoAccess,

    /// <summary>The bot lacks a permission it needs in the channel; the detail names them.</summary>
    MissingPermissions,

    /// <summary>The webhook URL was deleted or is not a webhook.</summary>
    WebhookInvalid,

    /// <summary>Discord couldn't be reached from this PC.</summary>
    DiscordUnreachable,

    /// <summary>The server folder has no server.properties yet (the server never started once).</summary>
    ServerPropertiesMissing,

    /// <summary>server.properties must change but the server is running, and its files are where it runs.</summary>
    ServerRunning,

    /// <summary>This app has no bridge executable for that platform (a local build that skipped the publish script).</summary>
    BinaryUnavailable,

    /// <summary>The VPS is not a Linux on x86_64 or ARM64.</summary>
    UnsupportedHost,

    /// <summary>The VPS could not be reached or signed in to.</summary>
    VpsUnreachable,

    /// <summary>Writing files on the host failed (server.properties, the executable or its config).</summary>
    CopyFailed,

    /// <summary>The background process, task or service could not be set up.</summary>
    RegisterFailed,

    /// <summary>The bridge started and ended at once; the detail holds its last log lines.</summary>
    BridgeExited,

    /// <summary>The bridge runs but did not answer the health check.</summary>
    NoAnswer,
}
