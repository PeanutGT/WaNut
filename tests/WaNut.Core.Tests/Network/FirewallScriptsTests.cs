using System.Text;
using WaNut.Core.Network;

namespace WaNut.Core.Tests.Network;

public sealed class FirewallScriptsTests
{
    private const string Java = @"C:\Users\花生\AppData\Roaming\WaNut\runtimes\java-21\bin\java.exe";

    [Theory]
    [InlineData(@"C:\a b\java.exe", @"'C:\a b\java.exe'")]
    [InlineData("C:\\it's\\java.exe", "'C:\\it''s\\java.exe'")]
    [InlineData("C:\\it\u2019s\\java.exe", "'C:\\it\u2019\u2019s\\java.exe'")]
    [InlineData("C:\\$(evil)\\java.exe", "'C:\\$(evil)\\java.exe'")]
    public void Quote_AnyPath_StaysOneLiteralString(string value, string expected)
    {
        Assert.Equal(expected, FirewallScripts.Quote(value));
    }

    [Fact]
    public void AllowScript_AddsAllowRuleForAllProfilesAndRemovesOnlyThisProgramsBlockRules()
    {
        var script = FirewallScripts.AllowScript(Java, "WaNut - Java 21");

        Assert.Contains($"$program = '{Java}'", script, StringComparison.Ordinal);
        Assert.Contains("-DisplayName 'WaNut - Java 21'", script, StringComparison.Ordinal);
        Assert.Contains("-Direction Inbound -Action Allow -Program $program -Profile Any", script, StringComparison.Ordinal);
        Assert.Contains("Get-NetFirewallApplicationFilter -Program $program", script, StringComparison.Ordinal);
        Assert.Contains("$_.Action -eq 'Block'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Encode_IsUtf16Base64()
    {
        var encoded = FirewallScripts.Encode("'花生'");

        Assert.Equal("'花生'", Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
    }

    [Theory]
    [InlineData("rule 2 0\nWANUT-OK\n", FirewallState.Allowed)]
    [InlineData("rule 2 2\r\nrule 2 4\r\nWANUT-OK\r\n", FirewallState.Allowed)]
    [InlineData("rule 2 6\nWANUT-OK", FirewallState.Allowed)]
    [InlineData("rule 2 2\nWANUT-OK\n", FirewallState.NotAllowed)]
    [InlineData("WANUT-OK\n", FirewallState.NotAllowed)]
    [InlineData("rule 2 0\nrule 4 4\nWANUT-OK\n", FirewallState.Blocked)]
    [InlineData("rule 2 0\n", FirewallState.Unknown)]
    [InlineData("", FirewallState.Unknown)]
    public void ParseState_Rules_ReturnsWhetherFriendsGetThrough(string output, FirewallState expected)
    {
        Assert.Equal(expected, FirewallScripts.ParseState(output));
    }
}
