using WaNut.Core.Jvm;

namespace WaNut.Core.Tests.Jvm;

public sealed class CustomJvmArgumentsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseCustom_Empty_GivesNoArguments(string? text)
    {
        Assert.Empty(JvmArguments.ParseCustom(text, out var problem)!);
        Assert.Equal(CustomArgumentProblem.None, problem);
    }

    [Fact]
    public void ParseCustom_SplitsOnSpacesAndKeepsQuotedSpaces()
    {
        var args = JvmArguments.ParseCustom("  -XX:+UseLargePages   -Dpath=\"C:\\My Folder\"\n-Dx=1 ", out _);

        Assert.Equal(["-XX:+UseLargePages", "-Dpath=C:\\My Folder", "-Dx=1"], args);
    }

    [Theory]
    [InlineData("server.jar", CustomArgumentProblem.NotAnOption)]
    [InlineData("-XX:+UseZGC nogui", CustomArgumentProblem.NotAnOption)]
    [InlineData("-", CustomArgumentProblem.NotAnOption)]
    [InlineData("-Xmx8G", CustomArgumentProblem.Memory)]
    [InlineData("-Xms1G", CustomArgumentProblem.Memory)]
    [InlineData("-jar", CustomArgumentProblem.NotAllowed)]
    [InlineData("-cp x", CustomArgumentProblem.NotAllowed)]
    [InlineData("-version", CustomArgumentProblem.NotAllowed)]
    [InlineData("-Dname=\"open", CustomArgumentProblem.UnclosedQuote)]
    public void ParseCustom_RejectsWhatWouldBreakTheStart(string text, CustomArgumentProblem expected)
    {
        Assert.Null(JvmArguments.ParseCustom(text, out var problem));
        Assert.Equal(expected, problem);
    }

    [Fact]
    public void Build_CustomArguments_ComeAfterWaNutsFlagsAndBeforeTheJar()
    {
        var args = JvmArguments.Build(21, 2048, "server.jar", ["-XX:+UseLargePages"]);

        Assert.Equal(["-XX:+UseLargePages", "-jar", "server.jar", "nogui"], args.TakeLast(4));
        Assert.True(args.ToList().IndexOf("-XX:+UseZGC") < args.ToList().IndexOf("-XX:+UseLargePages"));
    }
}
