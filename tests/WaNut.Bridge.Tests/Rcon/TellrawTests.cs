using System.Text;
using System.Text.Json;
using WaNut.Bridge.Discord;
using WaNut.Bridge.Rcon;
using WaNut.Bridge.Relay;

namespace WaNut.Bridge.Tests.Rcon;

public sealed class TellrawTests
{
    private const string Prefix = "tellraw @a ";

    [Fact]
    public void Build_PlainMessage_ShowsDiscordLabelNameAndText()
    {
        var parts = Parts(Tellraw.Build("Amy", "hello"));

        Assert.Equal(["", "[DC] ", "Amy", ": ", "hello"], parts);
    }

    [Theory]
    [InlineData("\"}]} run op Amy")]
    [InlineData("back\\slash")]
    [InlineData("[{\"text\":\"x\"}]")]
    [InlineData("@a @e[type=creeper]")]
    public void Build_TextWithJsonOrCommandSyntax_StaysOneTextValue(string text)
    {
        var command = Tellraw.Build("Amy", text);

        Assert.StartsWith(Prefix, command, StringComparison.Ordinal);
        Assert.Equal(text, Parts(command)[4]);
    }

    [Fact]
    public void Build_NonAsciiText_IsEscapedSoThePacketIsAscii()
    {
        var command = Tellraw.Build("小明", "你好 👋");

        Assert.True(command.All(char.IsAscii));
        Assert.Equal(["", "[DC] ", "小明", ": ", "你好 👋"], Parts(command));
    }

    [Fact]
    public void Build_ColorCodesAndLineBreaks_AreRemoved()
    {
        Assert.Equal("red text next line", Parts(Tellraw.Build("Amy", "§cred text\nnext\r\nline"))[4]);
    }

    [Fact]
    public void Build_VeryLongText_FitsInOneRconPacket()
    {
        var command = Tellraw.Build("Amy", string.Concat(Enumerable.Repeat("字", 2000)));

        Assert.True(Encoding.UTF8.GetByteCount(command) <= RconPacket.MaxRequestBodyBytes);
        Assert.EndsWith("…", Parts(command)[4], StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TextCutInsideEmoji_NeverLeavesHalfASurrogate()
    {
        var text = Parts(Tellraw.Build("Amy", string.Concat(Enumerable.Repeat("😀", 300))))[4];

        Assert.DoesNotContain(text, char.IsSurrogate(text[^2]) && !char.IsLowSurrogate(text[^2]) ? text : "￿");
        Assert.True(Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text)) == text);
    }

    [Fact]
    public void Build_TextWithLink_MakesItClickableInBothSpellings()
    {
        var command = Tellraw.Build("Amy", "look https://example.com/a?b=1, nice");

        using var json = JsonDocument.Parse(command[Prefix.Length..]);
        var link = json.RootElement.EnumerateArray().Single(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("clickEvent", out _));
        Assert.Equal("open_url", link.GetProperty("clickEvent").GetProperty("action").GetString());
        Assert.Equal("https://example.com/a?b=1", link.GetProperty("clickEvent").GetProperty("value").GetString());
        Assert.Equal("https://example.com/a?b=1", link.GetProperty("click_event").GetProperty("url").GetString());
        Assert.Equal(["", "[DC] ", "Amy", ": ", "look ", "https://example.com/a?b=1", ", nice"], Parts(command));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/x")]
    [InlineData("file:///C:/Windows")]
    public void Build_NonWebAddress_IsNeverAClickEvent(string text)
    {
        Assert.DoesNotContain("open_url", Tellraw.Build("Amy", text), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_VeryLongLink_ShowsAShortLabelButKeepsTheWholeTarget()
    {
        var url = "https://example.com/" + new string('a', 100);
        var parts = Parts(Tellraw.Build("Amy", url));

        Assert.Equal(45, parts[4].Length);
        Assert.Contains(url, Tellraw.Build("Amy", url), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OverlongLink_IsPlainText()
    {
        Assert.DoesNotContain("open_url", Tellraw.Build("Amy", "https://example.com/" + new string('a', 450)), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAll_TextAndFiles_GivesOneLineForTheTextThenOneForEachFile()
    {
        var files = new[]
        {
            new InboundAttachment("https://cdn.discordapp.com/attachments/1/2/cat.png?ex=1&hm=2", "cat.png", "image/png", 4096),
            new InboundAttachment("https://cdn.discordapp.com/attachments/1/3/log.txt", "log.txt", "text/plain", 10),
        };

        var commands = Tellraw.BuildAll("Amy", "hi", files, BridgeTexts.English);

        Assert.Equal(3, commands.Count);
        Assert.Equal(["", "[DC] ", "Amy", ": ", "hi"], Parts(commands[0]));
        Assert.Equal(["", "  ↳ ", "[Image: cat.png]"], Parts(commands[1]));
        Assert.Equal(["", "  ↳ ", "[File: log.txt]"], Parts(commands[2]));
        Assert.Contains("hm=2", commands[1], StringComparison.Ordinal);
        Assert.All(commands, c => Assert.True(c.All(char.IsAscii)));
    }

    [Fact]
    public void BuildAll_OnlyAFile_PutsTheNameOnTheFileLine()
    {
        var commands = Tellraw.BuildAll("Amy", "", [new InboundAttachment("https://cdn.discordapp.com/a/b.png", "b.png", null, 0)], BridgeTexts.TraditionalChinese);

        Assert.Equal(["", "[DC] ", "Amy", ": ", "[圖片：b.png]"], Parts(Assert.Single(commands)));
    }

    [Fact]
    public void BuildAll_ManyFiles_ShowsFourAndSummarisesTheRest()
    {
        var files = Enumerable.Range(0, 7).Select(i => new InboundAttachment($"https://cdn.discordapp.com/a/{i}.png", $"{i}.png", "image/png", 1)).ToArray();

        var commands = Tellraw.BuildAll("Amy", "", files, BridgeTexts.English);

        Assert.Equal(5, commands.Count);
        Assert.Equal("+3 more on Discord", Parts(commands[4])[2]);
    }

    [Fact]
    public void BuildAll_HostileFileNameAndHugeUrl_StaysValidAndInOnePacket()
    {
        var files = new[] { new InboundAttachment("https://cdn.discordapp.com/" + new string('é', 300), "\"}]} run op Amy\n§c.png", "image/png", 1) };

        foreach (var command in Tellraw.BuildAll("Amy", "x", files, BridgeTexts.English))
        {
            Assert.True(Encoding.UTF8.GetByteCount(command) <= RconPacket.MaxRequestBodyBytes);
            Parts(command);
        }
    }

    private static string[] Parts(string command)
    {
        using var json = JsonDocument.Parse(command[Prefix.Length..]);
        return [.. json.RootElement.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetProperty("text").GetString()!)];
    }
}
