using System.Text.Json;
using WaNut.Bridge.Discord;

namespace WaNut.Bridge.Tests.Discord;

public sealed class SlashCommandDefinitionsTests
{
    [Fact]
    public void Build_Both_ListForEveryoneCmdForAdminsWithRequiredOption()
    {
        using var json = JsonDocument.Parse(SlashCommandDefinitions.Build(list: true, console: true));
        var commands = json.RootElement.EnumerateArray().ToList();

        Assert.Equal(["list", "cmd", "status"], commands.Select(c => c.GetProperty("name").GetString()));
        Assert.False(commands[0].TryGetProperty("default_member_permissions", out _));
        Assert.Equal("0", commands[1].GetProperty("default_member_permissions").GetString());
        Assert.Equal("0", commands[2].GetProperty("default_member_permissions").GetString());
        var option = commands[1].GetProperty("options")[0];
        Assert.True(option.GetProperty("required").GetBoolean());
        Assert.Equal("查看 Minecraft 線上玩家", commands[0].GetProperty("description_localizations").GetProperty("zh-TW").GetString());
    }

    [Fact]
    public void Build_ConsoleOff_RegistersNoCmd()
    {
        using var json = JsonDocument.Parse(SlashCommandDefinitions.Build(list: true, console: false));

        Assert.Equal(["list", "status"], json.RootElement.EnumerateArray().Select(c => c.GetProperty("name").GetString()));
    }
}
