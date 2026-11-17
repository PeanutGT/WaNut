using System.Text;

namespace WaNut.Core.Bridge;

/// <summary>What a pasted "bot token" turned out to be, judged by its shape alone (nothing is sent to Discord).</summary>
public enum DiscordTokenShape
{
    /// <summary>Three dot-separated parts of letters, digits, "-" and "_", like a real bot token.</summary>
    BotToken,

    /// <summary>Only digits: the Application ID or another snowflake.</summary>
    ApplicationId,

    /// <summary>64 hex characters: the application's Public Key.</summary>
    PublicKey,

    /// <summary>One run of letters and digits with no dots: the OAuth2 Client Secret.</summary>
    ClientSecret,

    /// <summary>Anything else: wrong characters (full-width letters, "…"), or not three parts.</summary>
    Malformed,
}

/// <summary>
/// Cleans up a pasted bot token and tells apart the things people paste by mistake from the Discord Developer Portal.
/// A bot token is three parts joined by two dots (user id, time, signature), 59 characters or more in total.
/// </summary>
public static class DiscordTokenFormat
{
    private const string BotPrefix = "Bot ";

    /// <summary>
    /// Removes what a paste often brings along: spaces and line breaks (even inside the text), surrounding quotes or
    /// backticks, and a leading "Bot " (the word Discord's docs put in front of the token in the Authorization header).
    /// </summary>
    public static string Normalize(string? pasted)
    {
        if (string.IsNullOrWhiteSpace(pasted))
        {
            return string.Empty;
        }

        var text = pasted.Trim().Trim('"', '\'', '`', '“', '”', '‘', '’');
        if (text.StartsWith(BotPrefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[BotPrefix.Length..];
        }

        var clean = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c) && c != '​' && c != '﻿')
            {
                clean.Append(c);
            }
        }

        return clean.ToString().Trim('"', '\'', '`', '“', '”', '‘', '’');
    }

    /// <param name="token">Already passed through <see cref="Normalize"/>.</param>
    public static DiscordTokenShape Classify(string token)
    {
        if (token.Length == 0 || token.Any(c => !IsTokenChar(c) && c != '.'))
        {
            return DiscordTokenShape.Malformed;
        }

        if (token.All(char.IsAsciiDigit))
        {
            return DiscordTokenShape.ApplicationId;
        }

        if (token.Length == 64 && token.All(char.IsAsciiHexDigit))
        {
            return DiscordTokenShape.PublicKey;
        }

        if (!token.Contains('.'))
        {
            return DiscordTokenShape.ClientSecret;
        }

        var parts = token.Split('.');
        return parts.Length == 3 && parts.All(p => p.Length > 0) && token.Length >= 50
            ? DiscordTokenShape.BotToken
            : DiscordTokenShape.Malformed;
    }

    private static bool IsTokenChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';
}
