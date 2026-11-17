namespace WaNut.Core.Bridge;

/// <summary>What a deployer and the running bridge report back to the app.</summary>
public sealed record BridgeStatus
{
    public static BridgeStatus NotInstalled { get; } = new() { State = BridgeState.NotInstalled };

    public BridgeState State { get; init; }

    public int? ProcessId { get; init; }

    /// <summary>The bridge is connected to the server's RCON port.</summary>
    public bool RconConnected { get; init; }

    public bool DiscordConnected { get; init; }

    public int? DiscordLatencyMs { get; init; }

    /// <summary>The Minecraft server must restart once before RCON works (see <see cref="RconSetupResult.RestartNeeded"/>).</summary>
    public bool ServerRestartNeeded { get; init; }

    /// <summary>A short reason, shown when <see cref="State"/> is <see cref="BridgeState.Failed"/> or <see cref="BridgeState.Unknown"/>.</summary>
    public string? Message { get; init; }

    public DateTimeOffset? ReportedAt { get; init; }
}
