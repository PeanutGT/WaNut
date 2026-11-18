using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using WaNut.Bridge.Discord;
using WaNut.Bridge.Relay;

namespace WaNut.Bridge.Rcon;

/// <summary>
/// Builds the <c>tellraw @a</c> command that shows a Discord message in game as "[DC] name: text". The text
/// component is written by <see cref="Utf8JsonWriter"/>, never pasted into a string, so quotes, brackets or
/// backslashes in a message cannot change the command. Every non-ASCII character is escaped as \uXXXX, which
/// Minecraft reads back exactly and which keeps the RCON packet plain ASCII on every server version.
/// </summary>
public static partial class Tellraw
{
    public const int MaxNameLength = 32;

    public const int MaxTextLength = 256;

    private const string Prefix = "tellraw @a ";

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.Default };

    /// <summary>Most files shown as their own line; the rest are summarised so a burst of uploads cannot flood chat.</summary>
    public const int MaxAttachmentLines = 4;

    /// <summary>A link longer than this, or with non-ASCII characters (six bytes each once escaped), is shown as plain text, so it cannot fill the packet.</summary>
    private const int MaxLinkLength = 400;

    private const int MaxLinkLabelLength = 45;

    /// <summary>The replied-to name and excerpt are cut short; the excerpt is written twice (both hover spellings), so it must stay small.</summary>
    private const int MaxReplyNameLength = 20;

    private const int MaxReplyExcerptLength = 40;

    public static string Build(string author, string text) => Build(author, text, BridgeTexts.English);

    /// <summary>The chat line for a message; web addresses in the text become links that open in the player's browser.</summary>
    public static string Build(string author, string text, BridgeTexts texts, InboundReply? reply = null)
    {
        var name = Clean(author, MaxNameLength);
        var body = Clean(text, MaxTextLength);
        while (true)
        {
            var command = Prefix + Component(name, body, null, texts, reply: reply);
            if (Encoding.UTF8.GetByteCount(command) <= RconPacket.MaxRequestBodyBytes)
            {
                return command;
            }

            if (body.Length <= 1)
            {
                // Even a one-character text does not fit with the hint; the hint is the part to give up.
                return reply is null ? command : Build(author, text, texts);
            }

            // Escaped non-ASCII text takes six bytes a character; shorten until the packet fits.
            body = Cut(body, body.Length * 3 / 4) + "…";
        }
    }

    /// <summary>
    /// Every command for one Discord message: the chat line, then one line for each attached file so a long CDN address never
    /// shares a packet with the text. A message with only files gets its name on the first file line.
    /// </summary>
    public static IReadOnlyList<string> BuildAll(string author, string text, IReadOnlyList<InboundAttachment> attachments, BridgeTexts texts, InboundReply? reply = null)
    {
        var commands = new List<string>();
        var hasText = Clean(text, MaxTextLength).Length > 0;
        if (hasText || attachments.Count == 0)
        {
            commands.Add(Build(author, text, texts, reply));
        }

        var name = Clean(author, MaxNameLength);
        for (var i = 0; i < Math.Min(attachments.Count, MaxAttachmentLines); i++)
        {
            commands.Add(Prefix + Component(name, string.Empty, attachments[i], texts, withName: !hasText && i == 0, reply: !hasText && i == 0 ? reply : null));
        }

        if (attachments.Count > MaxAttachmentLines)
        {
            commands.Add(Prefix + Component(name, string.Empty, null, texts, more: attachments.Count - MaxAttachmentLines));
        }

        return commands;
    }

    private static string Component(string name, string text, InboundAttachment? file, BridgeTexts texts, bool withName = true, int more = 0, InboundReply? reply = null)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            json.WriteStartArray();
            json.WriteStringValue(string.Empty);
            if (withName && file is null && more == 0 || withName && file is not null)
            {
                WritePart(json, "[DC] ", "#5865F2");
                WritePart(json, name, "aqua");
                if (reply is not null)
                {
                    WriteReply(json, reply, texts);
                }

                WritePart(json, ": ", "gray");
            }
            else
            {
                WritePart(json, "  ↳ ", "dark_gray");
            }

            if (file is not null)
            {
                WriteFile(json, file, texts);
            }
            else if (more > 0)
            {
                WritePart(json, string.Format(System.Globalization.CultureInfo.InvariantCulture, texts.MoreFormat, more), "gray");
            }
            else
            {
                WriteText(json, text, texts);
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>" (reply to Bob)" after the author's name; hovering shows the start of the message that was answered.</summary>
    private static void WriteReply(Utf8JsonWriter json, InboundReply reply, BridgeTexts texts)
    {
        var label = " " + string.Format(System.Globalization.CultureInfo.InvariantCulture, texts.ReplyFormat, Clean(reply.AuthorName, MaxReplyNameLength));
        var excerpt = Clean(reply.Excerpt, MaxReplyExcerptLength);
        if (excerpt.Length == 0)
        {
            WritePart(json, label, "dark_gray");
            return;
        }

        json.WriteStartObject();
        json.WriteString("text", label);
        json.WriteString("color", "dark_gray");
        WriteHover(json, excerpt);
        json.WriteEndObject();
    }

    private static void WriteText(Utf8JsonWriter json, string text, BridgeTexts texts)
    {
        if (text.Length == 0)
        {
            WritePart(json, text, null);
            return;
        }

        var last = 0;
        foreach (Match match in UrlPattern().Matches(text))
        {
            var url = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '\'', '"');
            if (!CanOpen(url, out _))
            {
                continue;
            }

            if (match.Index > last)
            {
                WritePart(json, text[last..match.Index], null);
            }

            var label = url.Length > MaxLinkLabelLength ? url[..(MaxLinkLabelLength - 1)] + "…" : url;
            WriteLink(json, label, url, "aqua", texts.OpenHint + "\n" + (url.Length > 100 ? url[..99] + "…" : url));
            last = match.Index + url.Length;
        }

        if (last < text.Length)
        {
            WritePart(json, text[last..], null);
        }
    }

    private static void WriteFile(Utf8JsonWriter json, InboundAttachment file, BridgeTexts texts)
    {
        var fileName = Clean(file.FileName, 80);
        var label = string.Format(System.Globalization.CultureInfo.InvariantCulture, file.IsImage ? texts.ImageFormat : texts.FileFormat, fileName);
        if (CanOpen(file.Url, out _))
        {
            WriteLink(json, label, file.Url, file.IsImage ? "green" : "gold", texts.OpenHint + (file.Size > 0 ? $" ({Math.Max(1, file.Size / 1024)} KB)" : string.Empty));
        }
        else
        {
            WritePart(json, label, file.IsImage ? "green" : "gold");
        }
    }

    /// <summary>Only http and https addresses become links; Minecraft refuses anything else, and the player confirms the click anyway.</summary>
    private static bool CanOpen(string url, out Uri? uri)
    {
        uri = null;
        return url.Length <= MaxLinkLength && url.All(char.IsAscii) && !url.EndsWith('…') && Uri.TryCreate(url, UriKind.Absolute, out uri)
            && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host);
    }

    /// <summary>
    /// A clickable piece of text. Minecraft 1.21.5 renamed <c>clickEvent</c> and <c>hoverEvent</c> and their fields; every version ignores
    /// keys it does not know, so both spellings are written and the same command works from 1.16.5 on.
    /// </summary>
    private static void WriteLink(Utf8JsonWriter json, string label, string url, string color, string hint)
    {
        json.WriteStartObject();
        json.WriteString("text", label);
        json.WriteString("color", color);
        json.WriteBoolean("underlined", true);
        json.WriteStartObject("clickEvent");
        json.WriteString("action", "open_url");
        json.WriteString("value", url);
        json.WriteEndObject();
        json.WriteStartObject("click_event");
        json.WriteString("action", "open_url");
        json.WriteString("url", url);
        json.WriteEndObject();
        WriteHover(json, hint);
        json.WriteEndObject();
    }

    /// <summary>The hover text in both spellings, like <see cref="WriteLink"/>.</summary>
    private static void WriteHover(Utf8JsonWriter json, string hint)
    {
        json.WriteStartObject("hoverEvent");
        json.WriteString("action", "show_text");
        json.WriteString("contents", hint);
        json.WriteEndObject();
        json.WriteStartObject("hover_event");
        json.WriteString("action", "show_text");
        json.WriteString("value", hint);
        json.WriteEndObject();
    }

    [GeneratedRegex(@"https?://[^\s<>""]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

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
