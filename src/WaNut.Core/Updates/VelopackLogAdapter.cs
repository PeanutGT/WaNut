using Velopack.Logging;

namespace WaNut.Core.Updates;

/// <summary>
/// Sends Velopack's own messages (download, delta rebuild, swap of the app folder, failures) to update.log, so a
/// failed update leaves a reason behind instead of silently keeping the old version.
/// </summary>
public sealed class VelopackLogAdapter(UpdateLog log) : IVelopackLogger
{
    public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
    {
        if (logLevel == VelopackLogLevel.Trace)
        {
            return;
        }

        var text = exception is null ? message : $"{message} ({exception.GetType().Name}: {exception.Message})";
        log.Write($"Velopack {logLevel}: {text}");
    }
}
