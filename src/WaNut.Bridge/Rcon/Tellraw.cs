using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace WaNut.Bridge.Rcon;

/// <summary>
/// Builds the <c>tellraw @a</c> command that shows a Discord message in game as "[DC] name: text". The text
/// component is written by <see cref="Utf8JsonWriter"/>, never pasted into a string, so quotes, brackets or
/// backslashes in a message cannot change the command. Every non-ASCII character is escaped as \uXXXX, which
/// Minecraft reads back exactly and which keeps the RCON packet plain ASCII on every server version.
/// </summary>
public static class Tellraw
{
    public const int MaxNameLength = 32;

    public const int MaxTextLength = 256;

    private const string Prefix = "tellraw @a ";

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.Default };

    public static string Build(string author, string text)
    {
        var name = Clean(author, MaxNameLength);
        var body = Clean(text, MaxTextLength);
        while (true)
        {
            var command = Prefix + Component(name, body);
            if (Encoding.UTF8.GetByteCount(command) <= RconPacket.MaxRequestBodyBytes || body.Length <= 1)
            {
                return command;
            }

            // Escaped non-ASCII text takes six bytes a character; shorten until the packet fits.
            body = Cut(body, body.Length * 3 / 4) + "…";
        }
    }

    private static string Component(string name, string text)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            json.WriteStartArray();
            json.WriteStringValue(string.Empty);
            WritePart(json, "[DC] ", "#5865F2");
            WritePart(json, name, "aqua");
            WritePart(json, ": ", "gray");
            WritePart(json, text, null);
            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WritePart(Utf8JsonWriter json, string text, string? color)
    {
        json.WriteStartObject();
        json.WriteString("text", text);
        if (color is not null)
        {
            json.WriteString("color", color);
        }

        json.WriteEndObject();
    }

    /// <summary>
    /// Drops Minecraft's old color codes (the section sign and the character after it) and control characters, and puts a multi-line Discord
    /// message on one chat line.
    /// </summary>
    private static string Clean(string text, int maxLength)
    {
        var clean = new StringBuilder(Math.Min(text.Length, maxLength));
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '§')
            {
                i++;
            }
            else if (c is '\n' or '\r' or '\t')
            {
                if (clean.Length > 0 && clean[^1] != ' ')
                {
                    clean.Append(' ');
                }
            }
            else if (!char.IsControl(c))
            {
                clean.Append(c);
            }
        }

        var result = clean.ToString().Trim();
        return result.Length > maxLength ? Cut(result, maxLength - 1) + "…" : result;
    }

    /// <summary>The first <paramref name="length"/> characters, never ending in half a surrogate pair.</summary>
    private static string Cut(string text, int length)
    {
        if (length <= 0)
        {
            return string.Empty;
        }

        if (length >= text.Length)
        {
            return text;
        }

        return char.IsHighSurrogate(text[length - 1]) ? text[..(length - 1)] : text[..length];
    }
}
