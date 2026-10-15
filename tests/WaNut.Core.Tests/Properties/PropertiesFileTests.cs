using WaNut.Core.Properties;

namespace WaNut.Core.Tests.Properties;

public sealed class PropertiesFileTests
{
    private const string ServerProperties =
        "#Minecraft server properties\r\n" +
        "#Tue Oct 14 23:56:00 CST 2026\r\n" +
        "difficulty=easy\r\n" +
        "gamemode=survival\r\n" +
        "motd=A Minecraft Server\r\n" +
        "level-seed=\r\n" +
        "some-mod-setting=keep me\r\n";

    [Fact]
    public void Get_ExistingKey_ReturnsValue()
    {
        var file = PropertiesFile.Parse(ServerProperties);

        Assert.Equal("easy", file.Get("difficulty"));
        Assert.Equal("A Minecraft Server", file.Get("motd"));
        Assert.Equal(string.Empty, file.Get("level-seed"));
    }

    [Fact]
    public void Get_MissingKey_ReturnsNull()
    {
        Assert.Null(PropertiesFile.Parse(ServerProperties).Get("pvp"));
    }

    [Fact]
    public void Set_ExistingKey_ChangesOnlyThatLine()
    {
        var file = PropertiesFile.Parse(ServerProperties);

        file.Set("difficulty", "hard");

        Assert.Equal(ServerProperties.Replace("difficulty=easy", "difficulty=hard", StringComparison.Ordinal), file.ToText());
    }

    [Fact]
    public void Set_NewKey_AppendsIt()
    {
        var file = PropertiesFile.Parse(ServerProperties);

        file.Set("pvp", "false");

        Assert.EndsWith("some-mod-setting=keep me\r\npvp=false\r\n", file.ToText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("key=value", "value")]
    [InlineData("key = value", "value")]
    [InlineData("key:value", "value")]
    [InlineData("key value", "value")]
    [InlineData("  key=value", "value")]
    [InlineData("key=a=b", "a=b")]
    [InlineData(@"key=\u4f60\u597d", "你好")]
    [InlineData(@"key=line\nbreak", "line\nbreak")]
    [InlineData(@"key=back\\slash", @"back\slash")]
    [InlineData(@"key=\#not a comment", "#not a comment")]
    public void Get_JavaSyntax_ReadsLikeJava(string line, string expected)
    {
        Assert.Equal(expected, PropertiesFile.Parse(line).Get("key"));
    }

    [Theory]
    [InlineData("# key=value")]
    [InlineData("! key=value")]
    [InlineData("   ")]
    public void Get_CommentOrBlank_IsIgnored(string line)
    {
        Assert.Null(PropertiesFile.Parse(line).Get("key"));
    }

    [Fact]
    public void Get_ContinuedLine_JoinsLines()
    {
        var file = PropertiesFile.Parse("key=one \\\n    two\nother=x\n");

        Assert.Equal("one two", file.Get("key"));
        Assert.Equal("x", file.Get("other"));
    }

    [Fact]
    public void Set_ContinuedLine_ReplacesAllItsLines()
    {
        var file = PropertiesFile.Parse("key=one \\\n    two\nother=x\n");

        file.Set("key", "new");

        Assert.Equal("key=new\nother=x\n", file.ToText());
    }

    [Fact]
    public void Get_DuplicateKey_LastOneWins()
    {
        Assert.Equal("2", PropertiesFile.Parse("key=1\nkey=2\n").Get("key"));
    }

    [Theory]
    [InlineData("Peanut's 伺服器 §6gold", @"Peanut's \u4f3a\u670d\u5668 \u00a76gold")]
    [InlineData(" leading space", @"\ leading space")]
    [InlineData("a=b:c#d!e", @"a\=b\:c\#d\!e")]
    [InlineData(@"C:\worlds", @"C\:\\worlds")]
    public void Set_SpecialCharacters_EscapesAndRoundTrips(string value, string written)
    {
        var file = PropertiesFile.Parse(string.Empty);

        file.Set("motd", value);

        Assert.Equal($"motd={written}", file.ToText().TrimEnd());
        Assert.Equal(value, PropertiesFile.Parse(file.ToText()).Get("motd"));
    }

    [Fact]
    public void Keys_ListsEachKeyOnce()
    {
        Assert.Equal(["difficulty", "gamemode", "motd", "level-seed", "some-mod-setting"], PropertiesFile.Parse(ServerProperties).Keys);
    }

    [Fact]
    public void Load_MissingFile_StartsEmpty()
    {
        var file = PropertiesFile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "server.properties"));

        Assert.Empty(file.Keys);
    }
}
