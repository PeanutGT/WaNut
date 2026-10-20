using WaNut.Core.Backups;

namespace WaNut.Core.Tests.Backups;

public class BackupScheduleTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Fact]
    public void IsDue_SomeonePlayedForAnInterval_ReturnsTrue()
    {
        var schedule = new BackupSchedule();
        schedule.Update(online: true, playerCount: 0, Start);
        schedule.Update(online: true, playerCount: 1, Start.AddMinutes(20));

        Assert.False(schedule.IsDue(Start.AddMinutes(59), Hour));
        Assert.True(schedule.IsDue(Start.AddMinutes(60), Hour));
    }

    [Fact]
    public void IsDue_PlayerLeftBeforeTheInterval_StillReturnsTrue()
    {
        var schedule = new BackupSchedule();
        schedule.Update(online: true, playerCount: 1, Start);
        schedule.Update(online: true, playerCount: 0, Start.AddMinutes(10));

        Assert.True(schedule.IsDue(Start.AddMinutes(60), Hour));
    }

    [Fact]
    public void IsDue_NobodyPlayed_SkipsAndStartsOver()
    {
        var schedule = new BackupSchedule();
        schedule.Update(online: true, playerCount: 0, Start);

        Assert.False(schedule.IsDue(Start.AddMinutes(60), Hour));

        schedule.Update(online: true, playerCount: 1, Start.AddMinutes(61));
        Assert.False(schedule.IsDue(Start.AddMinutes(100), Hour));
        Assert.True(schedule.IsDue(Start.AddMinutes(120), Hour));
    }

    [Fact]
    public void Done_WaitsAFullIntervalAgain()
    {
        var schedule = new BackupSchedule();
        schedule.Update(online: true, playerCount: 1, Start);
        schedule.Done(Start.AddMinutes(60));
        schedule.Update(online: true, playerCount: 1, Start.AddMinutes(61));

        Assert.False(schedule.IsDue(Start.AddMinutes(119), Hour));
        Assert.True(schedule.IsDue(Start.AddMinutes(120), Hour));
    }

    [Fact]
    public void Update_Offline_StopsTheClock()
    {
        var schedule = new BackupSchedule();
        schedule.Update(online: true, playerCount: 1, Start);
        schedule.Update(online: false, playerCount: 0, Start.AddMinutes(30));

        Assert.False(schedule.IsDue(Start.AddMinutes(90), Hour));
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(0, 0)]
    [InlineData(45, 0)]
    [InlineData(-30, 0)]
    public void Normalized_UnknownSchedule_TurnsItOff(int minutes, int expected)
    {
        Assert.Equal(expected, new BackupRules { ScheduleMinutes = minutes }.Normalized().ScheduleMinutes);
    }
}
