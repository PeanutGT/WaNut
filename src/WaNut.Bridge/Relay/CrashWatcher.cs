using WaNut.Core.Crashes;

namespace WaNut.Bridge.Relay;

/// <summary>
/// Notices a crash in <c>latest.log</c> and works out why with the same <see cref="CrashAnalyzer"/> the WaNut window
/// uses. Keeps the last lines, flags a few lines only a crash writes, and stays quiet for <see cref="Cooldown"/>
/// afterwards so a server stuck in a restart loop posts once, not every time.
/// </summary>
public sealed class CrashWatcher(TimeProvider? time = null)
{
    public const int KeptLines = 300;

    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    private static readonly string[] Triggers =
    [
        "This crash report has been saved to:",
        "Encountered an unexpected exception",
        "Exception in server tick loop",
        "Failed to start the minecraft server",
        "FAILED TO BIND TO PORT",
        "Incompatible mods found!",
        "java.lang.OutOfMemoryError",
    ];

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Queue<string> _lines = new();
    private readonly object _gate = new();
    private DateTimeOffset? _lastAlert;

    /// <summary>Remembers the line; true when it is the first crash sign since the cooldown ended.</summary>
    public bool OnLine(string line)
    {
        lock (_gate)
        {
            _lines.Enqueue(line);
            while (_lines.Count > KeptLines)
            {
                _lines.Dequeue();
            }

            if (!Triggers.Any(t => line.Contains(t, StringComparison.Ordinal)))
            {
                return false;
            }

            var now = _time.GetUtcNow();
            if (_lastAlert is { } last && now - last < Cooldown)
            {
                return false;
            }

            _lastAlert = now;
            return true;
        }
    }

    /// <summary>
    /// Reads the kept lines and the newest crash report in <c>crash-reports</c> next to <c>logs</c>; null when
    /// nothing known shows up (the alert then still says the server crashed).
    /// </summary>
    public CrashDiagnosis? Diagnose(string logFile, DateTime sinceUtc)
    {
        string[] lines;
        lock (_gate)
        {
            lines = [.. _lines];
        }

        string? report = null;
        try
        {
            var serverDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(logFile)));
            var path = serverDir is null ? null : CrashAnalyzer.FindCrashReport(serverDir, sinceUtc);
            report = path is null ? null : File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // The log is enough.
        }

        return CrashAnalyzer.Analyze(lines, report);
    }
}
