using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaNut.Core.Hotplug;

/// <summary>One line of the audit file on the VPS: who hot-plugged what, when, and how it ended.</summary>
/// <param name="Log">The console's error lines behind a rollback.</param>
public sealed record HotplugAuditEntry(
    [property: JsonPropertyName("time")] DateTimeOffset Time,
    [property: JsonPropertyName("machine")] string Machine,
    [property: JsonPropertyName("outcome")] HotplugState Outcome,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("packs")] IReadOnlyList<HotplugAuditPack> Packs,
    [property: JsonPropertyName("log"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Log = null)
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    public static HotplugAuditEntry From(HotplugReport report, DateTimeOffset when) =>
        new(when.ToUniversalTime(), new string(Environment.MachineName.Where(c => !char.IsControl(c)).ToArray()), report.Outcome, report.Message,
            report.Items.Select(i => new HotplugAuditPack(i.FileName, i.Action, i.Sha256)).ToList(),
            report.ConsoleLines is { Count: > 0 } lines ? lines : null);

    /// <summary>One line of JSON, ending in a newline.</summary>
    public string ToLine() => JsonSerializer.Serialize(this, Options) + "\n";

    /// <summary>The entries of an audit file, newest first; lines that don't parse are skipped.</summary>
    public static IReadOnlyList<HotplugAuditEntry> ParseAll(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(TryParse)
            .OfType<HotplugAuditEntry>()
            .Reverse()
            .ToList();

    private static HotplugAuditEntry? TryParse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<HotplugAuditEntry>(line, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
