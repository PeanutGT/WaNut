using WaNut.Core.Hotplug.Readiness;

namespace WaNut.Core.Hotplug;

/// <summary>
/// Watch and hot-plug mode: waits for zips in a folder on this PC to settle (debounce window, no timeout), then
/// hot-plugs the batch, and goes back to waiting. A pack is applied again only once its file changes, whether or
/// not the last try worked, so a broken pack doesn't loop. <see cref="TriggerNow"/> skips the rest of the wait.
/// </summary>
public sealed class HotplugWatcher(DatapackHotplugOrchestrator orchestrator, string folder, Func<IReadOnlyList<string>, HotplugRequest> request, ReadinessOptions? options = null)
{
    private readonly AppliedFilterSource _source = new(new LocalReadinessSource(folder));
    private readonly ReadinessOptions _options = (options ?? new ReadinessOptions()) with { Timeout = null };
    private CancellationTokenSource? _wait;

    public string Folder => folder;

    /// <summary>Stops waiting for the batch to settle and applies the zips that are there now.</summary>
    public void TriggerNow() => _wait?.Cancel();

    /// <summary>Runs until <paramref name="cancellationToken"/> is cancelled. Each finished run is handed to <paramref name="finished"/>.</summary>
    public async Task RunAsync(Action<HotplugReport> finished, IProgress<HotplugProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ReadinessEntry> batch;
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                _wait = wait;
                try
                {
                    var ready = await orchestrator.WaitForDataAsync(_source, _options, progress, wait.Token).ConfigureAwait(false);
                    if (!ready.IsReady)
                    {
                        // Only a bad manifest gets here (there is no timeout); wait for it to change.
                        finished(new HotplugReport(HotplugState.Aborted, ready.Message, [], []));
                        _source.MarkApplied(await _source.SnapshotAsync(cancellationToken).ConfigureAwait(false));
                        continue;
                    }

                    batch = ready.Files;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    batch = await _source.SnapshotAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _wait = null;
                }
            }

            if (batch.Count == 0)
            {
                continue;
            }

            var paths = batch.Select(f => Path.Combine(folder, f.Name)).ToList();
            HotplugReport report;
            try
            {
                report = await orchestrator.RunAsync(request(paths), progress, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _source.MarkApplied(batch);
            }

            finished(report);
        }
    }
}
