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

    public string ServerJar { get; init; } = "server.jar";

    public required DateTimeOffset CreatedAt { get; init; }
}
