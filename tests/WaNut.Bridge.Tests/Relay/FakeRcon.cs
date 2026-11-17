using WaNut.Bridge.Rcon;

namespace WaNut.Bridge.Tests.Relay;

internal sealed class FakeRcon(Func<string, string?> answer) : IRconCommands
{
    public List<string> Commands { get; } = [];

    public bool IsConnected => true;

    public Task<string?> TryExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        Commands.Add(command);
        return Task.FromResult(answer(command));
    }
}
