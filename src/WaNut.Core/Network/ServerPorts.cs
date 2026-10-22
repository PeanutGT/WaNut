namespace WaNut.Core.Network;

/// <summary>
/// Picks a port no other server uses, so several servers can run side by side (blueprint decision 5).
/// </summary>
public static class ServerPorts
{
    /// <summary>How far past the wanted port to look before giving up.</summary>
    public const int SearchRange = 100;

    /// <summary>
    /// <paramref name="wanted"/> when no other server has it and <paramref name="isFree"/> accepts it; otherwise the
    /// first port after it that is neither; null if none of the next <see cref="SearchRange"/> is.
    /// </summary>
    public static int? Pick(int wanted, IReadOnlyCollection<int> taken, Func<int, bool> isFree)
    {
        for (var port = wanted; port <= Math.Min(wanted + SearchRange, 65535); port++)
        {
            if (!taken.Contains(port) && isFree(port))
            {
                return port;
            }
        }

        return null;
    }
}
