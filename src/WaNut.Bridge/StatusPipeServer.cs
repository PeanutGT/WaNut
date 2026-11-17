using System.IO.Pipes;
using System.Text;
using WaNut.Core.Bridge;

namespace WaNut.Bridge;

/// <summary>
/// Answers <see cref="BridgeStatusPipe"/> requests until cancelled: the current status, or a stop request, which
/// is how WaNut stops the bridge on Windows (there is no SIGTERM to send there).
/// </summary>
public sealed class StatusPipeServer(string serverId, Func<BridgeStatus> status, Action stop, BridgeLog log)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var name = BridgeStatusPipe.PipeName(serverId);
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (IOException ex)
            {
                // Another bridge for the same server owns the name; this one still works, it just can't be asked.
                log.Warn($"Status pipe unavailable: {ex.Message}");
                return;
            }

            await using (pipe.ConfigureAwait(false))
            {
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    await AnswerAsync(pipe, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // The asker went away.
                }
            }
        }
    }

    private async Task AnswerAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(BridgeStatusPipe.DefaultTimeout);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        string? request;
        try
        {
            request = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var stopping = request?.Trim() == BridgeStatusPipe.StopRequest;
        var answer = status() with { State = stopping ? BridgeState.Stopped : BridgeState.Running };
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(BridgeStatusPipe.Serialize(answer) + "\n"), cancellationToken).ConfigureAwait(false);
        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (stopping)
        {
            log.Info("Asked to stop.");
            stop();
        }
    }
}
