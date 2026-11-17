namespace WaNut.Bridge;

/// <summary>
/// The log file of a bridge with no journal to write to (a background process on Windows or a VPS without
/// systemd). Kept to about <see cref="MaxBytes"/>: a bigger one is moved to ".old" when the bridge starts.
/// </summary>
public static class BridgeLogFile
{
    public const long MaxBytes = 5 * 1024 * 1024;

    public static StreamWriter Open(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        if (File.Exists(full) && new FileInfo(full).Length > MaxBytes)
        {
            File.Move(full, full + ".old", overwrite: true);
        }

        // Others may read it while it is written; WaNut shows its last lines when the bridge fails.
        var stream = new FileStream(full, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        return new StreamWriter(stream) { AutoFlush = true };
    }
}
