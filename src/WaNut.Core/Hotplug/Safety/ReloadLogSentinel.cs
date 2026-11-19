using WaNut.Core.Hosting;

namespace WaNut.Core.Hotplug.Safety;

/// <summary>
/// Reads the console for <see cref="Window"/> after /reload (8 s by default) and reports the lines that say a data pack
/// failed to load (<see cref="ReloadLogPatterns"/>). Every line printed until the watch is disposed counts, so a reload
/// that takes longer than the window is still covered up to the "datapack list" answer that follows it.
/// </summary>
public sealed class ReloadLogSentinel(TimeProvider? time = null) : IReloadSentinel
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(8);

    /// <summary>How long more lines are gathered after the first error, so the summary holds the whole burst.</summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(1);

    /// <summary>At most this many lines are kept for the summary.</summary>
    public const int MaxLines = 20;

    private const int MaxLineLength = 400;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public TimeSpan Window { get; init; } = DefaultWindow;

    public TimeSpan Grace { get; init; } = DefaultGrace;

    public IReloadWatch Begin(IServerConsole console) => new Watch(this, console);

    private sealed class Watch : IReloadWatch
    {
        private readonly ReloadLogSentinel _owner;
        private readonly IServerConsole _console;
        private readonly DateTimeOffset _end;
        private readonly object _gate = new();
        private readonly List<string> _lines = [];
        private readonly TaskCompletionSource _failed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DateTimeOffset? _firstFailure;
        private int _errors;
        private bool _following;

        public Watch(ReloadLogSentinel owner, IServerConsole console)
        {
            _owner = owner;
            _console = console;
            _end = owner._time.GetUtcNow() + owner.Window;
            console.OutputReceived += OnLine;
        }

        public bool HasFailed => _failed.Task.IsCompleted;

        public async Task<ReloadFailure?> FailureAsync(CancellationToken cancellationToken)
        {
            var time = _owner._time;
            var left = _end - time.GetUtcNow();
            if (!HasFailed && left > TimeSpan.Zero)
            {
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                await Task.WhenAny(_failed.Task, Task.Delay(left, time, stop.Token)).ConfigureAwait(false);
                await stop.CancelAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!HasFailed)
            {
                return null;
            }

            // The first error rarely comes alone (one per broken file, plus stack traces); wait a moment for the rest.
            var graceLeft = _firstFailure!.Value + _owner.Grace - time.GetUtcNow();
            if (graceLeft > TimeSpan.Zero)
            {
                await Task.Delay(graceLeft, time, cancellationToken).ConfigureAwait(false);
            }

            lock (_gate)
            {
                var lines = _lines.ToList();
                var first = ReloadLogPatterns.Message(lines[0]);
                var summary = _errors == 1
                    ? $"The server reported an error while reloading: {Shorten(first, 200)}"
                    : $"The server reported {_errors} errors while reloading; the first: {Shorten(first, 200)}";
                return new ReloadFailure(summary, lines);
            }
        }

        public void Dispose() => _console.OutputReceived -= OnLine;

        private void OnLine(string line)
        {
            lock (_gate)
            {
                if (ReloadLogPatterns.IsFailure(line))
                {
                    _errors++;
                    _following = true;
                    _firstFailure ??= _owner._time.GetUtcNow();
                    Keep(line);
                    _failed.TrySetResult();
                }
                else if (_following && ReloadLogPatterns.IsContinuation(line))
                {
                    Keep(line);
                }
                else
                {
                    _following = false;
                }
            }
        }

        private void Keep(string line)
        {
            if (_lines.Count < MaxLines)
            {
                _lines.Add(Shorten(line, MaxLineLength));
            }
        }

        private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
