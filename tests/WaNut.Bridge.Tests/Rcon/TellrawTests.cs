using System.Text;
using System.Text.Json;
using WaNut.Bridge.Rcon;

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

    private static string[] Parts(string command)
    {
        using var json = JsonDocument.Parse(command[Prefix.Length..]);
        return [.. json.RootElement.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetProperty("text").GetString()!)];
    }
}
