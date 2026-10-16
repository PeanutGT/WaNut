using WaNut.Core.Hosting;

namespace WaNut.Core.Tests.Hosting;

public class RestartPolicyTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryRestart_ThreeCrashesInTenMinutes_AllowsThreeThenRefuses()
    {
        var policy = new RestartPolicy();

        Assert.True(policy.TryRestart(Start));
        Assert.True(policy.TryRestart(Start.AddMinutes(2)));
        Assert.True(policy.TryRestart(Start.AddMinutes(4)));
        Assert.False(policy.TryRestart(Start.AddMinutes(6)));
    }

    [Fact]
    public void TryRestart_OldRestartsLeaveTheWindow_AllowsAgain()
    {
        var policy = new RestartPolicy();
        policy.TryRestart(Start);
        policy.TryRestart(Start.AddMinutes(1));
        policy.TryRestart(Start.AddMinutes(2));

        Assert.True(policy.TryRestart(Start.AddMinutes(10)));
        Assert.Equal(3, policy.Count);
    }
}
