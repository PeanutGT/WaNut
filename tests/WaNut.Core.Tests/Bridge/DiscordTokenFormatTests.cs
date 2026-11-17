using WaNut.Core.Bridge;

namespace WaNut.Core.Tests.Bridge;

public sealed class DiscordTokenFormatTests
{
    private static readonly string Token = string.Join('.', "MTIzNDU2Nzg5MDEyMzQ1Njc4", "GabcDE", "abcdefghijklmnopqrstuvwxyz0123456789ABCD");

    [Theory]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData(" MTIz.abc.def \r\n", "MTIz.abc.def")]
    [InlineData("Bot MTIz.abc.def", "MTIz.abc.def")]
    [InlineData("BOT MTIz.abc.def", "MTIz.abc.def")]
    [InlineData("\"MTIz.abc.def\"", "MTIz.abc.def")]
    [InlineData("“MTIz.abc.def”", "MTIz.abc.def")]
    [InlineData("`MTIz.abc.def`", "MTIz.abc.def")]
    [InlineData("MTIz.abc\r\n.def", "MTIz.abc.def")]
    [InlineData("﻿MTIz.abc.def​", "MTIz.abc.def")]
    public void Normalize_StripsWhatAPasteBringsAlong(string? pasted, string expected) =>
        Assert.Equal(expected, DiscordTokenFormat.Normalize(pasted));

    [Fact]
    public void Classify_RealShapedToken_IsBotToken() =>
        Assert.Equal(DiscordTokenShape.BotToken, DiscordTokenFormat.Classify(Token));

    [Theory]
    [InlineData("", DiscordTokenShape.Malformed)]
    [InlineData("123456789012345678", DiscordTokenShape.ApplicationId)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", DiscordTokenShape.PublicKey)]
    [InlineData("abcDEF0123456789abcDEF0123456789", DiscordTokenShape.ClientSecret)]
    [InlineData("a.b", DiscordTokenShape.Malformed)]
    [InlineData("a.b.c.d", DiscordTokenShape.Malformed)]
    [InlineData("MTIz..ZZZZ", DiscordTokenShape.Malformed)]
    [InlineData("MTIz.abc.日本語", DiscordTokenShape.Malformed)]
    public void Classify_TellsApartWhatPeoplePasteByMistake(string text, DiscordTokenShape expected) =>
        Assert.Equal(expected, DiscordTokenFormat.Classify(text));
}
