namespace WaNut.Core.Downloads;

/// <param name="Remaining">Time left at the recent pace, or null while there is too little to go on.</param>
/// <param name="BytesPerSecond">Recent download speed, or null when the work isn't counted in bytes.</param>
public readonly record struct ProgressEstimate(TimeSpan? Remaining, double? BytesPerSecond);

/// <summary>
/// Guesses the time left for a download or other long job from how far it got over the last few seconds, so a
/// slow start or a burst doesn't swing the guess for long.
/// </summary>
public sealed class ProgressEstimator
{
    /// <summary>Only samples this recent count toward the pace.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(8);

    /// <summary>No guess until the job has been watched this long.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    private readonly Queue<Sample> _samples = new();
    private TimeSpan? _start;

    /// <summary>Forget everything, for a new download or a new stage.</summary>
    public void Reset()
    {
        _samples.Clear();
        _start = null;
    }

    /// <param name="at">Time since any fixed point, such as a stopwatch's elapsed time.</param>
    /// <param name="fraction">0 to 1.</param>
    /// <param name="bytes">Bytes done so far, when the job is a download.</param>
    public ProgressEstimate Add(TimeSpan at, double fraction, long? bytes = null)
    {
        _start ??= at;
        _samples.Enqueue(new Sample(at, fraction, bytes));
        while (_samples.Count > 2 && at - _samples.Peek().At > Window)
        {
            _samples.Dequeue();
        }

        var first = _samples.Peek();
        var seconds = (at - first.At).TotalSeconds;
        if (at - _start < Settle || seconds <= 0)
        {
            return default;
        }

        var pace = (fraction - first.Fraction) / seconds;
        TimeSpan? remaining = pace > 0 ? TimeSpan.FromSeconds(Math.Max(0, 1 - fraction) / pace) : null;
        double? speed = bytes is { } now && first.Bytes is { } then ? Math.Max(0, now - then) / seconds : null;
        return new ProgressEstimate(remaining, speed);
    }

    private readonly record struct Sample(TimeSpan At, double Fraction, long? Bytes);
}
