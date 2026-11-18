using System.Buffers;
using System.Text;
using System.Text.Json;

namespace WaNut.Core.Presence;

/// <summary>What the person's Discord profile shows. Never carries an IP address: the only button is the project page.</summary>
/// <param name="Details">The first line, the server's name.</param>
/// <param name="State">The second line, such as "Paper 1.21.4 · 3 online".</param>
/// <param name="StartedAt">Shows "elapsed" under the lines.</param>
public sealed record PresenceActivity(string Details, string State, DateTimeOffset StartedAt)
{
    public const string ButtonLabel = "WaNut";

    public const string ButtonUrl = "https://github.com/PeanutGT/WaNut";

    /// <summary>Discord drops lines under 2 and over 128 characters.</summary>
    public const int MaxLength = 128;

    /// <summary>The SET_ACTIVITY command; a null <paramref name="activity"/> clears the presence.</summary>
    public static string BuildCommand(PresenceActivity? activity, int processId, string nonce)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("cmd", "SET_ACTIVITY");
            json.WriteString("nonce", nonce);
            json.WriteStartObject("args");
            json.WriteNumber("pid", processId);
            if (activity is not null)
            {
                json.WriteStartObject("activity");
                json.WriteString("details", Fit(activity.Details));
                json.WriteString("state", Fit(activity.State));
                json.WriteStartObject("timestamps");
                json.WriteNumber("start", activity.StartedAt.ToUnixTimeSeconds());
                json.WriteEndObject();
                json.WriteStartArray("buttons");
                json.WriteStartObject();
                json.WriteString("label", ButtonLabel);
                json.WriteString("url", ButtonUrl);
                json.WriteEndObject();
                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string Fit(string text)
    {
        text = text.Trim();
        if (text.Length < 2)
        {
            text = text.PadRight(2);
        }

        return text.Length > MaxLength ? text[..(MaxLength - 1)] + "…" : text;
    }
}
