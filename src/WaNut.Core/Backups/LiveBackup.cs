using WaNut.Core.Hosting;

namespace WaNut.Core.Backups;

/// <summary>
/// Backs up the world of a running server. Automatic saving is turned off and the world flushed to disk first, so
/// the zip never catches a region file half-written; saving is turned back on afterwards, whatever happens.
/// </summary>
public static class LiveBackup
{
    /// <summary>How long the server gets to confirm "save-all flush" before the backup is given up.</summary>
    public static readonly TimeSpan SaveTimeout = TimeSpan.FromMinutes(2);

    // Servers running side by side share one disk: their backups take turns, and saving stays on while one waits.
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    public static async Task<BackupInfo?> CreateAsync(
        BackupStore store, string instanceDir, IServerConsole console, BackupReason reason,
        IProgress<double>? progress = null, TimeSpan? saveTimeout = null, CancellationToken cancellationToken = default)
    {
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLine(string line)
        {
            if (ServerLogParser.Parse(line)?.Kind == LogEventKind.Saved)
            {
                saved.TrySetResult();
            }
        }

        await OneAtATime.WaitAsync(cancellationToken);
        console.OutputReceived += OnLine;
        try
        {
            await console.SendCommandAsync("save-off", cancellationToken);
            await console.SendCommandAsync("save-all flush", cancellationToken);
            try
            {
                await saved.Task.WaitAsync(saveTimeout ?? SaveTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new BackupException(BackupProblem.SaveNotConfirmed, "save-all flush");
            }

            return await store.CreateAsync(instanceDir, reason, progress, cancellationToken);
        }
        finally
        {
            console.OutputReceived -= OnLine;
            try
            {
                await console.SendCommandAsync("save-on", CancellationToken.None);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException)
            {
                // The server has exited; it saves again on its next start anyway.
            }

            OneAtATime.Release();
        }
    }
}
