using System.Globalization;

namespace WaNut.Core.Diagnostics;

/// <summary>
/// The file WaNut writes unexpected errors to, so a problem nobody saw coming can still be looked up afterwards.
/// It stays on this PC and is never sent anywhere.
/// </summary>
public sealed class ErrorLog(string file, TimeProvider? time = null)
{
    /// <summary>Past this size the file becomes <c>.old</c> (replacing the last one) and a new one starts.</summary>
    public const long MaxBytes = 1024 * 1024;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();

    public string File { get; } = file;

    /// <summary>
    /// Adds the error with the time and where it was caught. Never throws: it runs while something else already
    /// failed, and a full disk must not turn that into a crash. Synchronous for the same reason: the process may be
    /// about to end. False when nothing could be written.
    /// </summary>
    public bool Write(string source, Exception error)
    {
        var stamp = _time.GetUtcNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var entry = $"[{stamp}Z] {source}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}";
        lock (_gate)
        {
            try
            {
                if (Path.GetDirectoryName(File) is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                }

                var info = new FileInfo(File);
                if (info.Exists && info.Length > MaxBytes)
                {
                    System.IO.File.Move(File, File + ".old", overwrite: true);
                }

                System.IO.File.AppendAllText(File, entry);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                return false;
            }
        }
    }
}
