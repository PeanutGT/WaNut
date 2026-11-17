using System.Buffers;
using System.Text;
using System.Text.Json;

namespace WaNut.Bridge.Discord;

/// <summary>Writes small JSON documents with <see cref="Utf8JsonWriter"/>, which needs no reflection in the trimmed bridge.</summary>
internal static class JsonText
{
    public static string Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            write(json);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Discord's "no pings" setting, so text from the game never mentions anyone.</summary>
    public static void WriteNoMentions(Utf8JsonWriter json)
    {
        json.WriteStartObject("allowed_mentions");
        json.WriteStartArray("parse");
        json.WriteEndArray();
        json.WriteEndObject();
    }
}
