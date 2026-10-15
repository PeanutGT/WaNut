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

    /// <summary>Paper build number or Fabric loader version; null for Vanilla.</summary>
    public string? LoaderVersion { get; init; }

    /// <summary>The jar java.exe runs. Fabric runs its launcher, which loads the Vanilla server.jar next to it.</summary>
    public string ServerJar { get; init; } = "server.jar";

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Game rules the user set in WaNut (keyed by <see cref="Settings.RuleSetting"/> name). They are sent to the
    /// server every time it comes online, because a game rule can only be changed while the server runs.
    /// </summary>
    public IReadOnlyDictionary<string, bool> GameRules { get; init; } = new Dictionary<string, bool>();

    /// <summary>Console commands waiting for the next start, such as "whitelist add Steve" added while stopped.</summary>
    public IReadOnlyList<string> PendingCommands { get; init; } = [];
}
