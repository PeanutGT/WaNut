using WaNut.Core.Hosting;

namespace WaNut.Core.Tests.Hotplug.Safety;

/// <summary>A console that answers each command with the lines <paramref name="answer"/> gives (none for unknown ones).</summary>
internal sealed class ScriptedConsole(Func<string, IEnumerable<string>> answer) : IServerConsole
{
    public event Action<string>? OutputReceived;

    public List<string> Commands { get; } = [];

    public Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        Commands.Add(command);
        foreach (var line in answer(command))
        {
            OutputReceived?.Invoke(line);
        }

        return Task.CompletedTask;
    }

    public void Print(string line) => OutputReceived?.Invoke(line);

    public static string Info(string message) => $"[12:00:00] [Server thread/INFO]: {message}";

    public static string Error(string message) => $"[12:00:00] [Worker-Main-2/ERROR]: {message}";
}
