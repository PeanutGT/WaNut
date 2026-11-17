using System.Xml.Linq;
using WaNut.Core.Bridge;

namespace WaNut.Core.Tests.Bridge;

/// <summary>The Discord tab builds resource keys from these enums, so a new value without text would show "[key]" to the user.</summary>
public sealed class DiscordTabStringsTests
{
    public static TheoryData<string> Keys()
    {
        var keys = new TheoryData<string>();
        foreach (var name in Enum.GetNames<BridgeDeployError>())
        {
            keys.Add("Discord_Error_" + name);
        }

        foreach (var name in Enum.GetNames<BridgeDeployStep>())
        {
            keys.Add("Discord_Step_" + name);
        }

        foreach (var name in Enum.GetNames<BridgeDeployWarning>())
        {
            keys.Add("Discord_Warning_" + name);
        }

        foreach (var name in Enum.GetNames<BridgeState>())
        {
            keys.Add("Discord_State_" + name);
        }

        return keys;
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void EnglishStrings_HaveTextForEveryEnumValue(string key)
    {
        var names = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "WaNut.App", "Resources", "Strings.resx"))
            .Root!.Elements("data").Select(e => (string?)e.Attribute("name")).ToHashSet();

        Assert.Contains(key, names);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WaNut.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("WaNut.slnx not found above the test folder.");
    }
}
