using System.Globalization;

namespace WaNut.Core.Bridge.Local;

/// <summary>
/// What runs for one server on this PC, saved as <c>bridge.run</c> next to its config: the way it runs, the executable
/// it runs from and, for a background process, its id. One line, "&lt;mode&gt; &lt;pid&gt; &lt;executable&gt;".
/// </summary>
public sealed record LocalBridgeRun(BridgeRunMode Mode, string Executable, int? ProcessId = null)
{
    public const string FileName = "bridge.run";

    public string ToText() =>
        string.Join(' ', Mode.ToString(), ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "-", Executable);

    /// <summary>Null for a damaged file.</summary>
    public static LocalBridgeRun? Parse(string text)
    {
        var parts = text.Trim().Split(' ', 3);
        if (parts.Length != 3 || !Enum.TryParse<BridgeRunMode>(parts[0], out var mode)
            || mode is not (BridgeRunMode.DetachedProcess or BridgeRunMode.ScheduledTask))
        {
            return null;
        }

        int? pid = int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
        return new LocalBridgeRun(mode, parts[2], pid);
    }
}
