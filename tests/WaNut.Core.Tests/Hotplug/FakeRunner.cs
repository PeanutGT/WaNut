using WaNut.Core.Remote.Files;

namespace WaNut.Core.Tests.Hotplug;

/// <summary>Answers each command with the next scripted result and remembers what was asked.</summary>
internal sealed class FakeRunner(params RemoteCommandResult[] answers) : IRemoteCommandRunner
{
    private readonly Queue<RemoteCommandResult> _answers = new(answers);

    public List<string> Commands { get; } = [];

    public Task<RemoteCommandResult> RunAsync(string command, string? input = null, CancellationToken cancellationToken = default)
    {
        Commands.Add(command);
        return Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : new RemoteCommandResult(0, string.Empty, string.Empty));
    }
}
