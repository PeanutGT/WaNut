namespace WaNut.Bridge.Rcon;

/// <summary>Runs server commands over RCON when the connection is up.</summary>
public interface IRconCommands
{
    bool IsConnected { get; }

    /// <summary>The command's output, or null when RCON is down or the command failed.</summary>
    Task<string?> TryExecuteAsync(string command, CancellationToken cancellationToken = default);
}
