using WaNut.Core.Remote.Files;

namespace WaNut.Core.Tests.Remote.Files;

public sealed class WorkspaceRootGuardTests
{
    private const string Home = "/home/ubuntu";

    [Theory]
    [InlineData("/")]
    [InlineData("/root")]
    [InlineData("/etc")]
    [InlineData("/usr")]
    [InlineData("/var")]
    [InlineData("/opt")]
    [InlineData("/var/lib")]
    [InlineData("/etc/minecraft")]
    [InlineData("/usr/local/mc")]
    [InlineData("/proc/1")]
    [InlineData("//etc/./")]
    [InlineData("relative/path")]
    [InlineData("/srv/../etc")]
    [InlineData("")]
    public void Find_SystemFolder_Refused(string root) =>
        Assert.Equal(WorkspaceRootProblem.SystemFolder, WorkspaceRootGuard.Find(root, Home));

    [Theory]
    [InlineData("/home")]
    [InlineData("/home/ubuntu")]
    [InlineData("/home/ubuntu/")]
    [InlineData("/home/someone")]
    public void Find_HomeFolder_Refused(string root) =>
        Assert.Equal(WorkspaceRootProblem.HomeFolder, WorkspaceRootGuard.Find(root, Home));

    [Fact]
    public void Find_RootUsersHome_Refused() =>
        Assert.Equal(WorkspaceRootProblem.HomeFolder, WorkspaceRootGuard.Find("/root", "/root"));

    [Theory]
    [InlineData("/home/ubuntu/wanut/servers/abc")]
    [InlineData("/home/ubuntu/mc")]
    [InlineData("/srv/minecraft")]
    [InlineData("/opt/minecraft")]
    [InlineData("/var/games/minecraft")]
    [InlineData("/root/mc")]
    public void Find_ServerFolder_Allowed(string root) =>
        Assert.Null(WorkspaceRootGuard.Find(root, Home));

    [Fact]
    public void Check_Refused_ThrowsWithProblem()
    {
        var e = Assert.Throws<WorkspaceRootException>(() => WorkspaceRootGuard.Check("/etc", Home));

        Assert.Equal(WorkspaceRootProblem.SystemFolder, e.Problem);
        Assert.Equal("/etc", e.Root);
    }
}
