using WaNut.Bridge.Discord;

namespace WaNut.Bridge.Tests.Discord;

public sealed class DiscordTextTests
{
    [Fact]
    public void ToPlain_Markup_BecomesReadableText()
    {
        var names = new Dictionary<ulong, string> { [42] = "Amy" };

        var text = DiscordText.ToPlain("hi <@42> and <@!7> in <#9> <@&5> <:pog:123> <a:dance:456>", names);

        Assert.Equal("hi @Amy and @user in #channel @role :pog: :dance:", text);
    }
}
