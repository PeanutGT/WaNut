namespace WaNut.Core.Hosting;

/// <summary>Tracks how long an online server has had nobody on it, for "stop when nobody is online".</summary>
public sealed class EmptyServerTimer
{
    private DateTimeOffset? _emptySince;

    /// <summary>Call regularly. The clock starts when the server is online with no players and resets otherwise.</summary>
    public void Update(bool online, int playerCount, DateTimeOffset now)
    {
        if (!online || playerCount > 0)
        {
            _emptySince = null;
        }
        else
        {
            _emptySince ??= now;
        }
    }

    /// <summary>True once the server has been online and empty for at least <paramref name="after"/>.</summary>
    public bool IsDue(DateTimeOffset now, TimeSpan after) => _emptySince is { } since && now - since >= after;
}
