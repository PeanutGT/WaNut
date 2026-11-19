namespace WaNut.Core.Hotplug;

/// <summary>What the lock file says about whoever holds it. A field the file lacks is null.</summary>
public sealed record HotplugLockHolder(int? Pid, string? Machine, DateTimeOffset? Since)
{
    /// <summary>Reads the key=value lines of a lock file; anything unreadable is left out.</summary>
    public static HotplugLockHolder Parse(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = line.IndexOf('=', StringComparison.Ordinal);
            if (at > 0)
            {
                values[line[..at]] = line[(at + 1)..].TrimEnd('\r');
            }
        }

        return new HotplugLockHolder(
            values.TryGetValue("pid", out var pid) && int.TryParse(pid, out var p) ? p : null,
            values.GetValueOrDefault("machine"),
            values.TryGetValue("time", out var time) && long.TryParse(time, out var t) ? DateTimeOffset.FromUnixTimeSeconds(t) : null);
    }
}
