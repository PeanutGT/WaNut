namespace WaNut.Core.Backups;

/// <summary>
/// How many world backups each server keeps (decision 10: the latest 5, none older than 30 days), and how often a
/// running server is backed up.
/// </summary>
public sealed record BackupRules
{
    public const int MinKeepCount = 1;
    public const int MaxKeepCount = 100;
    public const int MinMaxAgeDays = 1;
    public const int MaxMaxAgeDays = 3650;

    /// <summary>The schedule choices in minutes; 0 is off.</summary>
    public static readonly IReadOnlyList<int> ScheduleChoices = [0, 30, 60, 180, 360];

    public int KeepCount { get; init; } = 5;

    public int MaxAgeDays { get; init; } = 30;

    /// <summary>Minutes between backups while a server runs, one of <see cref="ScheduleChoices"/>. Off by default.</summary>
    public int ScheduleMinutes { get; init; }

    public static bool IsValidKeepCount(int value) => value is >= MinKeepCount and <= MaxKeepCount;

    public static bool IsValidMaxAgeDays(int value) => value is >= MinMaxAgeDays and <= MaxMaxAgeDays;

    /// <summary>Values from a hand-edited config.json are pulled back into range; an unknown schedule turns it off.</summary>
    public BackupRules Normalized() => new()
    {
        KeepCount = Math.Clamp(KeepCount, MinKeepCount, MaxKeepCount),
        MaxAgeDays = Math.Clamp(MaxAgeDays, MinMaxAgeDays, MaxMaxAgeDays),
        ScheduleMinutes = ScheduleChoices.Contains(ScheduleMinutes) ? ScheduleMinutes : 0,
    };
}
