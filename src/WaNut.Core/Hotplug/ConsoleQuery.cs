using WaNut.Core.Hosting;

namespace WaNut.Core.Hotplug;

/// <summary>Sends a console command and waits for the line that answers it.</summary>
public static class ConsoleQuery
{
    /// <summary>The first line after sending <paramref name="command"/> that <paramref name="isAnswer"/> accepts, or null after <paramref name="timeout"/>.</summary>
    public static async Task<string?> AskAsync(IServerConsole console, string command, Func<string, bool> isAnswer, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLine(string line)
        {
            if (isAnswer(line))
            {
                answer.TrySetResult(line);
            }
        }

        // Listen before sending, so a fast answer isn't missed.
        console.OutputReceived += OnLine;
        try
        {
            await console.SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timer.CancelAfter(timeout);
            try
            {
                return await answer.Task.WaitAsync(timer.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }
        finally
        {
            console.OutputReceived -= OnLine;
        }
    }

    /// <summary>The pack ids <c>datapack list enabled</c> names, or null when the server didn't answer in time.</summary>
    public static async Task<IReadOnlyList<string>?> EnabledPacksAsync(IServerConsole console, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var line = await AskAsync(console, "datapack list enabled", l => DatapackListParser.IsAnswer(l), timeout, cancellationToken).ConfigureAwait(false);
        return line is null ? null : DatapackListParser.Packs(line);
    }
}
