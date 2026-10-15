namespace WaNut.Core.Backups;

/// <summary>How many world backups each server keeps (decision 10: the latest 5, none older than 30 days).</summary>
public sealed record BackupRules
{
    public const int MinKeepCount = 1;
    public const int MaxKeepCount = 100;
    public const int MinMaxAgeDays = 1;
    public const int MaxMaxAgeDays = 3650;

    public int KeepCount { get; init; } = 5;

    public int MaxAgeDays { get; init; } = 30;

    public static bool IsValidKeepCount(int value) => value is >= MinKeepCount and <= MaxKeepCount;

    public static bool IsValidMaxAgeDays(int value) => value is >= MinMaxAgeDays and <= MaxMaxAgeDays;

    /// <summary>Values from a hand-edited config.json are pulled back into range.</summary>
    public BackupRules Normalized() => new()
    {
        KeepCount = Math.Clamp(KeepCount, MinKeepCount, MaxKeepCount),
        MaxAgeDays = Math.Clamp(MaxAgeDays, MinMaxAgeDays, MaxMaxAgeDays),
    };
}
