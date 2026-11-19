using WaNut.Core.Hotplug.Safety;
using static WaNut.Core.Tests.Hotplug.Safety.ScriptedConsole;

namespace WaNut.Core.Tests.Hotplug.Safety;

public sealed class ServerLoadProbeTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);

    [Theory]
    [InlineData("[12:00:00] [Server thread/INFO]: There are 3 of a max of 20 players online: a, b, c", 3)]
    [InlineData("[12:00:00 INFO]: There are 0 out of maximum 20 players online.", 0)]
    [InlineData("[12:00:00 INFO]: There are 1/20 players online:", 1)]
    [InlineData("[12:00:00] [Server thread/INFO]: <Steve> There are 9 of a max of 20 players online", null)]
    public void Players_ListAnswer_ReadsCount(string line, int? expected) => Assert.Equal(expected, ServerLoadProbe.Players(line));

    [Fact]
    public void FromMspt_PaperLine_ReadsFiveSecondAverage() =>
        Assert.Equal(47.3, ServerLoadProbe.FromMspt("[12:00:00 INFO]: ◴ 47.3/1.2/80.5, 30.0/1.1/12.0, 2.9/1.0/15.3"));

    [Fact]
    public void FromMspt_Header_Null() =>
        Assert.Null(ServerLoadProbe.FromMspt("[12:00:00 INFO]: Server tick times (avg/min/max) from last 5s, 10s, 1m:"));

    [Fact]
    public void FromTickQuery_VanillaLine_ReadsAverage() =>
        Assert.Equal(12.5, ServerLoadProbe.FromTickQuery("Average time per tick: 12.5ms (Target: 50.0ms)"));

    [Theory]
    [InlineData("[12:00:00 INFO]: TPS from last 1m, 5m, 15m: 18.2, 19.0, 20.0", 18.2)]
    [InlineData("[12:00:00 INFO]: TPS from last 1m, 5m, 15m: *20.0, *20.0, *20.0", 20.0)]
    public void FromTps_Line_ReadsOneMinute(string line, double expected) => Assert.Equal(expected, ServerLoadProbe.FromTps(line));

    [Fact]
    public async Task ReadAsync_Paper_UsesMspt()
    {
        var console = new ScriptedConsole(c => c switch
        {
            "list" => [Info("There are 4 of a max of 20 players online: a, b, c, d")],
            "mspt" => [Info("Server tick times (avg/min/max) from last 5s, 10s, 1m:"), Info("◴ 52.0/1.2/80.5, 30.0/1.1/12.0, 2.9/1.0/15.3")],
            _ => [],
        });

        var load = await ServerLoadProbe.ReadAsync(console, Short);

        Assert.Equal(new ServerLoad(4, 52.0, "mspt"), load);
        Assert.Equal(["list", "mspt"], console.Commands);
    }

    [Fact]
    public async Task ReadAsync_Vanilla_FallsBackToTickQuery()
    {
        var console = new ScriptedConsole(c => c switch
        {
            "list" => [Info("There are 0 of a max of 20 players online: ")],
            "mspt" => [Info("Unknown or incomplete command, see below for error")],
            "tick query" => [Info("The game is running normally"), "Target tick rate: 20.0 per second.", "Average time per tick: 3.2ms (Target: 50.0ms)"],
            _ => [],
        });

        var load = await ServerLoadProbe.ReadAsync(console, Short);

        Assert.Equal(new ServerLoad(0, 3.2, "tick query"), load);
    }

    [Fact]
    public async Task ReadAsync_SpigotAtFullSpeed_NoTickTime()
    {
        var console = new ScriptedConsole(c => c switch
        {
            "list" => [Info("There are 2/20 players online:")],
            "tps" => [Info("TPS from last 1m, 5m, 15m: 20.0, 20.0, 20.0")],
            _ => [Info("Unknown command. Type \"/help\" for help.")],
        });

        var load = await ServerLoadProbe.ReadAsync(console, Short);

        Assert.Equal(new ServerLoad(2, null, "tps"), load);
    }

    [Fact]
    public async Task ReadAsync_SpigotBehind_EstimatesFromTps()
    {
        var console = new ScriptedConsole(c => c switch
        {
            "tps" => [Info("TPS from last 1m, 5m, 15m: 16.0, 19.0, 20.0")],
            _ => [Info("Unknown command. Type \"/help\" for help.")],
        });

        var load = await ServerLoadProbe.ReadAsync(console, Short);

        Assert.Equal(62.5, load.Mspt);
        Assert.Null(load.Players);
    }

    [Fact]
    public async Task ReadAsync_NoAnswers_Unknown()
    {
        var load = await ServerLoadProbe.ReadAsync(new ScriptedConsole(_ => []), Short);

        Assert.Equal(new ServerLoad(null, null), load);
    }
}
