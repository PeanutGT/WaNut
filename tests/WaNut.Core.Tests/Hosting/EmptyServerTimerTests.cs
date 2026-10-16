using WaNut.Core.Hosting;

namespace WaNut.Core.Tests.Hosting;

public class EmptyServerTimerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    [Fact]
    public void IsDue_OnlineAndEmptyLongEnough_ReturnsTrue()
    {
        var timer = new EmptyServerTimer();
        timer.Update(online: true, playerCount: 0, Start);
        timer.Update(online: true, playerCount: 0, Start.AddMinutes(5));

        Assert.False(timer.IsDue(Start.AddMinutes(9), TenMinutes));
        Assert.True(timer.IsDue(Start.AddMinutes(10), TenMinutes));
    }

    [Fact]
    public void IsDue_SomeoneJoinedInBetween_StartsOver()
    {
        var timer = new EmptyServerTimer();
        timer.Update(online: true, playerCount: 0, Start);
        timer.Update(online: true, playerCount: 1, Start.AddMinutes(8));
        timer.Update(online: true, playerCount: 0, Start.AddMinutes(9));

        Assert.False(timer.IsDue(Start.AddMinutes(12), TenMinutes));
        Assert.True(timer.IsDue(Start.AddMinutes(19), TenMinutes));
    }

    [Fact]
    public void IsDue_StillStarting_NeverDue()
    {
        var timer = new EmptyServerTimer();
        timer.Update(online: false, playerCount: 0, Start);

        Assert.False(timer.IsDue(Start.AddHours(1), TenMinutes));
    }
}
