using System.Globalization;

namespace WaNut.Core.Hotplug.Readiness;

/// <summary>
/// Waits until a batch of files is completely there, so nothing is applied while an upload or a build is half done.
/// The inbox is looked at every <see cref="ReadinessOptions.PollInterval"/>. Any new, changed or removed file, and any
/// file that can't be opened exclusively, restarts the <see cref="ReadinessOptions.Debounce"/> countdown; the batch is
/// complete once it runs out. With a <see cref="ReadinessOptions.SentinelName"/> nothing counts until that file exists.
/// Cancel the token to stop waiting (it throws <see cref="OperationCanceledException"/>).
/// </summary>
public sealed class ReadinessGate(TimeProvider? time = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<ReadinessResult> WaitAsync(IReadinessSource source, ReadinessOptions? options = null, IProgress<ReadinessStatus>? progress = null, CancellationToken cancellationToken = default)
    {
        options ??= new ReadinessOptions();
        var sentinel = options.SentinelName;
        var start = _time.GetUtcNow();
        Dictionary<string, (long Size, DateTime Time)>? previous = null;
        var lastChange = start;
        var wasLocked = false;

        while (true)
        {
            var snapshot = await source.SnapshotAsync(cancellationToken).ConfigureAwait(false);
            var now = _time.GetUtcNow();
            var elapsed = now - start;

            var sentinelEntry = sentinel is null ? null : snapshot.FirstOrDefault(e => e.Name == sentinel);
            var data = snapshot.Where(e => e.Name != sentinel && !IsIgnored(e.Name, options)).ToList();

            // Any difference from the last look, including a file that vanished, restarts the countdown.
            var current = data.ToDictionary(e => e.Name, e => (e.SizeBytes, e.ModifiedUtc), StringComparer.Ordinal);
            if (sentinelEntry is not null)
            {
                current["\0sentinel"] = (sentinelEntry.SizeBytes, sentinelEntry.ModifiedUtc);
            }

            var changed = previous is null ? current.Count > 0 : !current.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(previous.OrderBy(p => p.Key, StringComparer.Ordinal));
            previous = current;
            if (changed)
            {
                lastChange = now;
            }

            string? waitingFor = null;
            IReadOnlyList<ReadinessEntry> batch = data;
            if (sentinel is not null && sentinelEntry is null)
            {
                waitingFor = sentinel;
            }
            else if (sentinelEntry is not null && !changed && sentinel!.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var names = ReadinessManifest.TryRead(await source.ReadTextAsync(sentinel, cancellationToken).ConfigureAwait(false), out var problem);
                if (names is null)
                {
                    return new ReadinessResult(ReadinessOutcome.InvalidManifest, [], problem);
                }

                var missing = names.FirstOrDefault(n => data.All(e => e.Name != n));
                if (missing is not null)
                {
                    waitingFor = missing;
                }

                batch = data.Where(e => names.Contains(e.Name, StringComparer.Ordinal)).ToList();
            }
            else if (data.Count == 0)
            {
                waitingFor = "files";
            }

            if (waitingFor is not null)
            {
                // Nothing to settle yet; the countdown starts when the awaited file shows up.
                lastChange = now;
                if (TimedOut(options, elapsed))
                {
                    return new ReadinessResult(ReadinessOutcome.TimedOut, [], $"Timed out after {Seconds(elapsed)} waiting for {waitingFor}.");
                }

                progress?.Report(new ReadinessStatus(ReadinessPhase.WaitingForData, elapsed, data.Count, Detail: waitingFor));
                await _delay(options.PollInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var locked = changed ? null : await FirstLockedAsync(source, batch, cancellationToken).ConfigureAwait(false);
            if (locked is not null || wasLocked)
            {
                // The moment a holder lets go counts as a change too: the file may be complete only now.
                lastChange = now;
            }

            wasLocked = locked is not null;

            if (changed || locked is not null)
            {
                if (TimedOut(options, elapsed))
                {
                    return new ReadinessResult(ReadinessOutcome.TimedOut, [], $"Timed out after {Seconds(elapsed)}: {locked ?? "the files"} kept changing.");
                }

                progress?.Report(new ReadinessStatus(ReadinessPhase.Settling, elapsed, data.Count, Detail: locked));
                await _delay(options.PollInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var remaining = options.Debounce - (now - lastChange);
            if (remaining <= TimeSpan.Zero)
            {
                return new ReadinessResult(ReadinessOutcome.Ready, batch);
            }

            if (TimedOut(options, elapsed))
            {
                return new ReadinessResult(ReadinessOutcome.TimedOut, [], $"Timed out after {Seconds(elapsed)} before the files stayed quiet.");
            }

            progress?.Report(new ReadinessStatus(ReadinessPhase.Debouncing, elapsed, data.Count, remaining));
            await _delay(remaining < options.PollInterval ? remaining : options.PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool TimedOut(ReadinessOptions options, TimeSpan elapsed) => options.Timeout is { } limit && elapsed >= limit;

    private static string Seconds(TimeSpan span) => span.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s";

    private static bool IsIgnored(string name, ReadinessOptions options) =>
        name.StartsWith('.') || options.IgnoredSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase));

    private static async Task<string?> FirstLockedAsync(IReadinessSource source, IReadOnlyList<ReadinessEntry> files, CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            if (!await source.CanReadExclusivelyAsync(file.Name, cancellationToken).ConfigureAwait(false))
            {
                return file.Name;
            }
        }

        return null;
    }
}
