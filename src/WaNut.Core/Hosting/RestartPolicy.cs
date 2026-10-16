namespace WaNut.Core.Hosting;

/// <summary>
/// Decides whether a crashed server is started again: at most <see cref="MaxRestarts"/> times within
/// <see cref="Window"/>, so a server that keeps crashing stops trying instead of looping forever.
/// </summary>
public sealed class RestartPolicy
{
    public const int MaxRestarts = 3;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly Queue<DateTimeOffset> _restarts = new();

    /// <summary>How many restarts the current window holds, including the one just allowed.</summary>
    public int Count => _restarts.Count;

    /// <summary>Records a restart and returns true, or returns false when the limit was already reached.</summary>
    public bool TryRestart(DateTimeOffset now)
    {
        while (_restarts.Count > 0 && now - _restarts.Peek() >= Window)
        {
            _restarts.Dequeue();
        }

        if (_restarts.Count >= MaxRestarts)
        {
            return false;
        }

        _restarts.Enqueue(now);
        return true;
    }
}
