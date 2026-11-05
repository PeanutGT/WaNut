using WaNut.Core.Remote;

namespace WaNut.Core.Tests.Remote;

public sealed class VpsProfileTests
{
    private static readonly VpsProfile Good = new() { Name = "Hetzner", Host = "203.0.113.5", User = "mc", Login = VpsLogin.GeneratedKey };

    [Fact]
    public void Validate_Complete_NoProblem()
    {
        Assert.Null(Good.Validate());
        Assert.Null((Good with { Host = "mc.example.com" }).Validate());
        Assert.Null((Good with { Host = "[2001:db8::1]" }).Validate());
    }

    [Fact]
    public void Validate_EachMissingField_NamesIt()
    {
        Assert.Equal(VpsProfileProblem.NoName, (Good with { Name = " " }).Validate());
        Assert.Equal(VpsProfileProblem.BadHost, (Good with { Host = "my host" }).Validate());
        Assert.Equal(VpsProfileProblem.BadHost, (Good with { Host = "" }).Validate());
        Assert.Equal(VpsProfileProblem.BadPort, (Good with { Port = 0 }).Validate());
        Assert.Equal(VpsProfileProblem.BadPort, (Good with { Port = 70000 }).Validate());
        Assert.Equal(VpsProfileProblem.NoUser, (Good with { User = "" }).Validate());
        Assert.Equal(VpsProfileProblem.NoKeyFile, (Good with { Login = VpsLogin.KeyFile }).Validate());
    }

    [Fact]
    public void NewProfiles_GetDifferentIdsOfLettersAndDigits()
    {
        var a = new VpsProfile();
        var b = new VpsProfile();

        Assert.NotEqual(a.Id, b.Id);
        Assert.True(a.Id.All(char.IsAsciiLetterOrDigit));
    }

    [Theory]
    [InlineData("mc.example.com", "mc.example.com:25565")]
    [InlineData(" 203.0.113.5 ", "203.0.113.5:25565")]
    [InlineData("[2001:db8::1]", "[2001:db8::1]:25565")]
    [InlineData("2001:db8::1", "[2001:db8::1]:25565")]
    public void Endpoint_Host_IsWhatFriendsType(string host, string expected) =>
        Assert.Equal(expected, new VpsProfile { Host = host }.Endpoint(25565));
}
