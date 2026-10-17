namespace WaNut.Core.Instances;

/// <summary>
/// What WaNut knows about one server. Stored as instance.json in the server's folder.
/// </summary>
public sealed record InstanceMetadata
{
    public const string FileName = "instance.json";

    /// <summary>Folder name under instances/; unique and stable.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required ServerType Type { get; init; }

    public required string MinecraftVersion { get; init; }

    public required int JavaMajor { get; init; }

    public required int MemoryMb { get; init; }

    /// <summary>Paper build number, or the Fabric, Forge or NeoForge loader version; null for Vanilla.</summary>
    public string? LoaderVersion { get; init; }

    /// <summary>
    /// The jar java.exe runs. Fabric runs its launcher, which loads the Vanilla server.jar next to it. Forge and
    /// NeoForge from 1.17 start from an argument file instead, stored as "@libraries/.../win_args.txt".
    /// </summary>
    public string ServerJar { get; init; } = "server.jar";

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Game rules the user set in WaNut (keyed by <see cref="Settings.GameSettingsStore.RuleKey"/>, e.g. "keepInventory"). They are sent to the
    /// server every time it comes online, because a game rule can only be changed while the server runs.
    /// </summary>
    public IReadOnlyDictionary<string, bool> GameRules { get; init; } = new Dictionary<string, bool>();

    /// <summary>Extra JVM options from Advanced mode, as the user typed them. Empty for none.</summary>
    public string CustomJvmArguments { get; init; } = string.Empty;

    /// <summary>Starts the server again when it crashes (at most 3 times in 10 minutes). On unless the user turned it off.</summary>
    public bool AutoRestart { get; init; } = true;

    /// <summary>Stops the server after this many minutes online with nobody on it. 0 means never.</summary>
    public int AutoStopMinutes { get; init; }

    /// <summary>Forwards the server's port on the home router (UPnP) while it runs. Off unless the user turned it on.</summary>
    public bool OpenRouterPort { get; init; }

    /// <summary>Runs a playit.gg tunnel while the server runs, so friends join through it. Off unless the user turned it on.</summary>
    public bool UsePlayitTunnel { get; init; }

    /// <summary>The server's own tunnel on playit.gg, made the first time the tunnel was used. Null until then.</summary>
    public Guid? PlayitTunnelId { get; init; }

    /// <summary>Console commands waiting for the next start, such as "whitelist add Steve" added while stopped.</summary>
    public IReadOnlyList<string> PendingCommands { get; init; } = [];
}
