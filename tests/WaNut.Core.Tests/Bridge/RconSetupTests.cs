using WaNut.Core.Bridge;
using WaNut.Core.Properties;

namespace WaNut.Core.Tests.Bridge;

public sealed class RconSetupTests
{
    private const string Vanilla =
        "#Minecraft server properties\r\n" +
        "#Tue Oct 14 23:56:00 CST 2026\r\n" +
        "difficulty=easy\r\n" +
        "enable-rcon=false\r\n" +
        "motd=A Minecraft Server\r\n" +
        "rcon.password=\r\n" +
        "rcon.port=25575\r\n" +
        "some-mod-setting=keep me\r\n";

    [Fact]
    public void Apply_VanillaFile_EnablesRconAndKeepsEverythingElseInPlace()
    {
        var file = PropertiesFile.Parse(Vanilla);

        var result = RconSetup.Apply(file);

        var lines = file.ToText().Split("\r\n");
        Assert.Equal("#Minecraft server properties", lines[0]);
        Assert.Equal("difficulty=easy", lines[2]);
        Assert.Equal("enable-rcon=true", lines[3]);
        Assert.Equal("motd=A Minecraft Server", lines[4]);
        Assert.Equal("rcon.password=" + result.Password, lines[5]);
        Assert.Equal("rcon.port=25575", lines[6]);
        Assert.Equal("some-mod-setting=keep me", lines[7]);
        Assert.Equal("rcon.ip=127.0.0.1", lines[8]);
        Assert.True(result.RestartNeeded);
        Assert.True(result.PasswordGenerated);
        Assert.Equal(25575, result.Port);
    }

    [Fact]
    public void Apply_EmptyPassword_GeneratesTwentyFourCharacters()
    {
        var result = RconSetup.Apply(PropertiesFile.Parse(Vanilla));

        Assert.Equal(24, result.Password.Length);
        Assert.All(result.Password, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
    }

    [Fact]
    public void Apply_TwoFiles_GetDifferentPasswords()
    {
        var first = RconSetup.Apply(PropertiesFile.Parse(Vanilla));
        var second = RconSetup.Apply(PropertiesFile.Parse(Vanilla));

        Assert.NotEqual(first.Password, second.Password);
    }

    [Fact]
    public void Apply_RunTwice_SecondRunChangesNothing()
    {
        var file = PropertiesFile.Parse(Vanilla);
        var first = RconSetup.Apply(file);
        var afterFirst = file.ToText();

        var second = RconSetup.Apply(file);

        Assert.Equal(afterFirst, file.ToText());
        Assert.False(second.RestartNeeded);
        Assert.False(second.PasswordGenerated);
        Assert.Equal(first.Password, second.Password);
    }

    [Fact]
    public void Apply_AlreadyConfigured_ChangesNothingAndNeedsNoRestart()
    {
        const string text = "enable-rcon=true\nrcon.ip=127.0.0.1\nrcon.port=30000\nrcon.password=hunter2\n";
        var file = PropertiesFile.Parse(text);

        var result = RconSetup.Apply(file);

        Assert.Equal(text, file.ToText());
        Assert.False(result.RestartNeeded);
        Assert.Equal(30000, result.Port);
        Assert.Equal("hunter2", result.Password);
    }

    [Fact]
    public void Apply_ExistingPassword_IsKept()
    {
        var file = PropertiesFile.Parse("enable-rcon=false\nrcon.password=my secret\n");

        var result = RconSetup.Apply(file);

        Assert.Equal("my secret", result.Password);
        Assert.False(result.PasswordGenerated);
        Assert.Equal("my secret", file.Get("rcon.password"));
    }

    [Fact]
    public void Apply_WhitespaceOnlyPassword_IsReplaced()
    {
        var file = PropertiesFile.Parse("rcon.password=\\ \\ \n");

        var result = RconSetup.Apply(file);

        Assert.True(result.PasswordGenerated);
        Assert.Equal(24, file.Get("rcon.password")!.Length);
    }

    [Fact]
    public void Apply_RconIpOpenToEveryone_IsForcedToLoopback()
    {
        var file = PropertiesFile.Parse("enable-rcon=true\nrcon.ip=0.0.0.0\nrcon.port=25575\nrcon.password=x\n");

        var result = RconSetup.Apply(file);

        Assert.Equal("127.0.0.1", file.Get("rcon.ip"));
        Assert.True(result.RestartNeeded);
    }

    [Theory]
    [InlineData("TRUE")]
    [InlineData("True")]
    [InlineData(" true ")]
    public void Apply_EnableRconTrueInOtherCase_IsLeftAlone(string value)
    {
        var file = PropertiesFile.Parse($"enable-rcon={value}\nrcon.ip=127.0.0.1\nrcon.port=25575\nrcon.password=x\n");
        var before = file.ToText();

        var result = RconSetup.Apply(file);

        Assert.Equal(before, file.ToText());
        Assert.False(result.RestartNeeded);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("")]
    public void Apply_EnableRconNotTrue_IsSetToTrue(string value)
    {
        var file = PropertiesFile.Parse($"enable-rcon={value}\n");

        RconSetup.Apply(file);

        Assert.Equal("true", file.Get("enable-rcon"));
    }

    [Fact]
    public void Apply_CommentedOutKeys_AreNotTreatedAsSet()
    {
        var file = PropertiesFile.Parse("# enable-rcon=true\n! rcon.password=old\nmotd=hi\n");

        var result = RconSetup.Apply(file);

        Assert.True(result.PasswordGenerated);
        var text = file.ToText();
        Assert.StartsWith("# enable-rcon=true\n! rcon.password=old\nmotd=hi\n", text, StringComparison.Ordinal);
        Assert.Equal("true", file.Get("enable-rcon"));
    }

    [Fact]
    public void Apply_DuplicateKeys_LastOneWinsAndEarlierLinesStay()
    {
        var file = PropertiesFile.Parse("enable-rcon=true\nenable-rcon=false\n");

        RconSetup.Apply(file);

        Assert.StartsWith("enable-rcon=true\nenable-rcon=true\n", file.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ColonAndSpaceSeparators_AreUnderstood()
    {
        var file = PropertiesFile.Parse("enable-rcon : false\nrcon.port 30000\nrcon.password:abc\n");

        var result = RconSetup.Apply(file);

        Assert.Equal("true", file.Get("enable-rcon"));
        Assert.Equal(30000, result.Port);
        Assert.Equal("abc", result.Password);
    }

    [Fact]
    public void Apply_PasswordOnContinuationLines_IsReadWhole()
    {
        var file = PropertiesFile.Parse("rcon.password=ab\\\n   cd\nmotd=x\n");

        var result = RconSetup.Apply(file);

        Assert.Equal("abcd", result.Password);
        Assert.False(result.PasswordGenerated);
        Assert.Contains("motd=x", file.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_PasswordWithEqualsAndUnicode_RoundTripsFromText()
    {
        var file = PropertiesFile.Parse("rcon.password=a\\=b\\u00e9\n");

        var result = RconSetup.Apply(file);

        Assert.Equal("a=bé", result.Password);
        Assert.Equal("a=bé", PropertiesFile.Parse(file.ToText()).Get("rcon.password"));
    }

    [Theory]
    [InlineData("rcon.port=abc")]
    [InlineData("rcon.port=0")]
    [InlineData("rcon.port=70000")]
    [InlineData("rcon.port=-5")]
    [InlineData("rcon.port=")]
    public void Apply_InvalidPort_IsReplacedByDefault(string line)
    {
        var file = PropertiesFile.Parse(line + "\n");

        var result = RconSetup.Apply(file);

        Assert.Equal(25575, result.Port);
        Assert.Equal("25575", file.Get("rcon.port"));
        Assert.True(result.RestartNeeded);
    }

    [Fact]
    public void Apply_NoPortAndPreferredPort_UsesPreferred()
    {
        var file = PropertiesFile.Parse("motd=x\n");

        var result = RconSetup.Apply(file, preferredPort: 25600);

        Assert.Equal(25600, result.Port);
        Assert.Equal("25600", file.Get("rcon.port"));
    }

    [Fact]
    public void Apply_ValidPortAndPreferredPort_KeepsFilePort()
    {
        var file = PropertiesFile.Parse("rcon.port=30000\n");

        Assert.Equal(30000, RconSetup.Apply(file, preferredPort: 25600).Port);
    }

    [Fact]
    public void Apply_EmptyFile_AddsAllFourKeys()
    {
        var file = PropertiesFile.Parse(string.Empty);

        var result = RconSetup.Apply(file);

        Assert.Equal(["enable-rcon", "rcon.ip", "rcon.port", "rcon.password"], file.Keys);
        Assert.True(result.RestartNeeded);
    }

    [Fact]
    public void Apply_NoTrailingNewline_KeepsLfLineEndingsAndAppends()
    {
        var file = PropertiesFile.Parse("motd=x\nenable-rcon=false");

        RconSetup.Apply(file);

        var text = file.ToText();
        Assert.DoesNotContain('\r', text);
        Assert.StartsWith("motd=x\nenable-rcon=true\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_CrLfFile_StaysCrLf()
    {
        var file = PropertiesFile.Parse(Vanilla);

        RconSetup.Apply(file);

        Assert.DoesNotContain("\n", file.ToText().Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_UnknownLinesAndBlankLines_AreKeptInOrder()
    {
        const string text = "\n# note\nweird line without equals\n\nkey.with.dots = spaced value\nenable-rcon=false\n";
        var file = PropertiesFile.Parse(text);

        RconSetup.Apply(file);

        Assert.StartsWith(
            "\n# note\nweird line without equals\n\nkey.with.dots = spaced value\nenable-rcon=true\n",
            file.ToText(),
            StringComparison.Ordinal);
    }
}
