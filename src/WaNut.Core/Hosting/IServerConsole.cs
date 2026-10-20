namespace WaNut.Core.Hosting;

/// <summary>A running server's console: the lines it prints and the commands sent to it.</summary>
public interface IServerConsole
{
    /// <summary>Raised for every console line, on a background thread.</summary>
    event Action<string>? OutputReceived;

    Task SendCommandAsync(string command, CancellationToken cancellationToken = default);
}
