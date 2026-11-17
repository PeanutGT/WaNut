using WaNut.Core.Bridge.Local;

namespace WaNut.Core.Tests.Bridge.Local;

public sealed class WindowsCommandLineTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData(@"C:\Program Files\x.exe", "\"C:\\Program Files\\x.exe\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("", "\"\"")]
    public void Quote_FollowsWindowsRules(string argument, string expected)
    {
        Assert.Equal(expected, WindowsCommandLine.Quote(argument));
    }
}
