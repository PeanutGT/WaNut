namespace WaNut.Core.Hotplug.Readiness;

/// <summary>How a <see cref="ReadinessGate"/> decides that the data is complete.</summary>
public sealed record ReadinessOptions
{
    /// <summary>Names that are never data: files still being written under a temporary name.</summary>
    public static readonly IReadOnlyList<string> DefaultIgnoredSuffixes = [".part", ".tmp", ".temp", ".partial", ".crdownload", ".wanut-part"];

    /// <summary>How often the inbox is looked at. A file counts as still growing until two looks in a row agree.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long the inbox must stay unchanged after the last change (a new file, a growing file) before the batch counts as complete.</summary>
    public TimeSpan Debounce { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>The longest to wait for everything; null waits until cancelled.</summary>
    public TimeSpan? Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A flag file the producer adds last: ".ready" (any content) means every other file is the batch, and a name ending
    /// in ".json" such as "manifest.json" is read for <c>{"files":["a.zip","b.zip"]}</c>, which names the batch.
    /// Null takes whatever files are there once they settle.
    /// </summary>
    public string? SentinelName { get; init; }

    /// <summary>Files ending in one of these are skipped (case-insensitive); so is any name starting with '.'.</summary>
    public IReadOnlyList<string> IgnoredSuffixes { get; init; } = DefaultIgnoredSuffixes;
}
