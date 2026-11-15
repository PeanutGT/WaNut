namespace WaNut.Core.Hosting;

/// <summary>
/// Notices a server that keeps the CPU busy while nobody plays: often a broken mod or plugin, a farm left running,
/// or something that should not be there. Says so once per run, and again only after someone has joined.
/// </summary>
public sealed class IdleCpuWatch
{
    /// <summary>CPU share of the whole PC: an empty server normally stays far below this.</summary>
    public const double Threshold = 25;

    /// <summary>How long it must stay that high, so a save or a short spike does not count.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(10);

    private DateTimeOffset? _since;
    private bool _warned;

    /// <summary>Feeds one reading. True once when the server has been empty and busy for <see cref="Duration"/>.</summary>
    public bool Update(bool online, int players, double cpuPercent, DateTimeOffset now)
    {
        if (players > 0)
        {
            _warned = false;
        }

        if (!online || players > 0 || cpuPercent < Threshold)
        {
            _since = null;
            return false;
        }

        _since ??= now;
        if (_warned || now - _since.Value < Duration)
        {
            return false;
        }

        _warned = true;
        return true;
    }

    /// <summary>The world is being generated on purpose (pregeneration), which is busy work, not a problem.</summary>
    public void Reset() => _since = null;

    /// <summary>A console line from a pregeneration tool such as Chunky, which works hard with nobody online.</summary>
    public static bool IsWorldGeneration(string line) =>
        line.Contains("[Chunky]", StringComparison.OrdinalIgnoreCase)
        || line.Contains("pregenerat", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Preparing spawn area", StringComparison.OrdinalIgnoreCase);
}
