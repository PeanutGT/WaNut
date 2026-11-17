using System.Globalization;

namespace WaNut.Bridge;

/// <summary>
/// Writes one line per event to standard output, where systemd's journal or the local launcher's log file picks it
/// up. Never give it a token, webhook URL or password.
/// </summary>
public sealed class BridgeLog(TextWriter? writer = null, TimeProvider? time = null)
{
    private readonly TextWriter _writer = writer ?? Console.Out;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _gate = new();

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var stamp = _time.GetUtcNow().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        lock (_gate)
        {
            _writer.WriteLine($"{stamp} [{level}] {message}");
            _writer.Flush();
        }
    }
}
