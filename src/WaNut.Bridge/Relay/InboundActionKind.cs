namespace WaNut.Bridge.Relay;

public enum InboundActionKind
{
    Ignore,

    /// <summary>Show the message in game.</summary>
    Chat,

    /// <summary>/list: answer with who is online.</summary>
    ListPlayers,

    /// <summary>/cmd: run a server command for an admin.</summary>
    Console,

    /// <summary>/cmd from someone without the admin role.</summary>
    ConsoleDenied,

    /// <summary>/status: put the live status message in the channel the command was used in.</summary>
    Status,

    /// <summary>/status from someone without the admin role.</summary>
    StatusDenied,

    /// <summary>A bridge command used outside the bridge channel.</summary>
    WrongChannel,
}
