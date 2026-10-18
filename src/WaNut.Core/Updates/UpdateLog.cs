namespace WaNut.Core.Updates;

/// <summary>
/// A short record of WaNut's own update checks (update.log in the data folder), so a check that failed quietly when
/// WaNut started can still be found. Only the newest lines are kept.
/// </summary>
public sealed class UpdateLog
{
    public const string FileName = "update.log";

    /// <summary>Lines kept after each write; older ones are dropped.</summary>
    public const int MaxLines = 200;

    private readonly TimeProvider _time;

    public UpdateLog(LauncherPaths paths, TimeProvider time)
    {
        FilePath = Path.Combine(paths.Root, FileName);
        _time = time;
    }

    public string FilePath { get; }

    /// <summary>Adds a timestamped line. Never throws: a log that can't be written must not break the update check.</summary>
    public void Write(string message)
    {
        try
        {
            var line = $"{_time.GetLocalNow():yyyy-MM-dd HH:mm:ss} {message.ReplaceLineEndings(" ")}";
            var lines = File.Exists(FilePath) ? File.ReadAllLines(FilePath).ToList() : [];
            lines.Add(line);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(FilePath, lines.Skip(Math.Max(0, lines.Count - MaxLines)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
