using System.Xml.Linq;
using WaNut.Core.Bridge.Local;

namespace WaNut.Core.Tests.Bridge.Local;

public sealed class BridgeTaskXmlTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void Build_StartsAtTheUsersSignInWithoutElevation()
    {
        var xml = XDocument.Parse(BridgeTaskXml.Build(@"PC\peanut", @"C:\b\WaNut.Bridge.exe", "--config \"C:\\a b\\c.json\"", "x"));

        Assert.Equal(@"PC\peanut", xml.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "UserId")!.Value);
        Assert.Equal("LeastPrivilege", xml.Descendants(Ns + "RunLevel").Single().Value);
        Assert.Equal("PT0S", xml.Descendants(Ns + "ExecutionTimeLimit").Single().Value);
        Assert.Equal(@"C:\b\WaNut.Bridge.exe", xml.Descendants(Ns + "Command").Single().Value);
        Assert.Equal("--config \"C:\\a b\\c.json\"", xml.Descendants(Ns + "Arguments").Single().Value);
    }

    [Fact]
    public void TaskName_IsInTheWaNutFolder()
    {
        Assert.Equal(@"WaNut\Discord bridge WaNut.Bridge.abc", BridgeTaskXml.TaskName("abc"));
    }
}
