namespace WaNut.Core.Backups;

/// <summary>
/// Decides when a running server is due for a scheduled backup. The clock starts when the server gets online,
/// right after the backup made before the start. A period in which nobody played is skipped, since the world
/// barely changed.
/// </summary>
public sealed class BackupSchedule
{
    private DateTimeOffset? _since;
    private bool _played;

    /// <summary>Call regularly while the server runs.</summary>
    public void Update(bool online, int playerCount, DateTimeOffset now)
    {
        if (!online)
        {
            _since = null;
            _played = false;
            return;
        }

        _since ??= now;
        _played |= playerCount > 0;
    }

    /// <summary>
    /// True once <paramref name="interval"/> has passed and someone played meanwhile. An interval nobody played in
    /// starts the clock again.
    /// </summary>
    public bool IsDue(DateTimeOffset now, TimeSpan interval)
    {
        if (_since is not { } since || now - since < interval)
        {
            return false;
        }

        if (!_played)
        {
            _since = now;
            return false;
        }

        return true;
    }

    /// <summary>A backup was taken (or tried): the next one is a full interval away.</summary>
    public void Done(DateTimeOffset now)
    {
        _since = now;
        _played = false;
    }
}
