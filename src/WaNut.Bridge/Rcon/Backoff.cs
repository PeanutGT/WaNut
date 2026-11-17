namespace WaNut.Bridge.Rcon;

/// <summary>Waits that double after each failure, up to a cap, and start over after a success.</summary>
public sealed class Backoff(TimeSpan initial, TimeSpan max)
{
    private int _failures;

    public TimeSpan Next()
    {
        var wait = initial * Math.Pow(2, Math.Min(_failures, 30));
        _failures++;
        return wait > max ? max : wait;
    }

    public void Reset() => _failures = 0;
}
