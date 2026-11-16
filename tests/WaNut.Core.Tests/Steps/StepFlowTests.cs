using WaNut.Core.Hosting;
using WaNut.Core.Instances;
using WaNut.Core.Steps;

namespace WaNut.Core.Tests.Steps;

public sealed class StepFlowTests
{
    [Fact]
    public void NewFlow_StartsOnFirstStep()
    {
        var flow = new StepFlow("Test", ["A", "B", "C"]);

        Assert.Equal("A", flow.CurrentStep);
        Assert.Equal([StepState.Current, StepState.Waiting, StepState.Waiting], States(flow));
    }

    [Fact]
    public void MoveTo_LaterStep_MarksSkippedStepsDone()
    {
        var flow = new StepFlow("Test", ["A", "B", "C", "D"]);

        Assert.True(flow.MoveTo("C"));

        Assert.Equal([StepState.Done, StepState.Done, StepState.Current, StepState.Waiting], States(flow));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("Unknown")]
    public void MoveTo_EarlierSameOrUnknownStep_StaysPut(string step)
    {
        var flow = new StepFlow("Test", ["A", "B", "C"]);
        flow.MoveTo("B");

        Assert.False(flow.MoveTo(step));
        Assert.Equal("B", flow.CurrentStep);
    }

    [Fact]
    public void Finish_MarksEveryStepDone_AndStopsMoves()
    {
        var flow = new StepFlow("Test", ["A", "B", "C"]);

        flow.Finish();

        Assert.True(flow.IsFinished);
        Assert.Equal("C", flow.CurrentStep);
        Assert.All(States(flow), s => Assert.Equal(StepState.Done, s));
        Assert.False(flow.MoveTo("C"));
    }

    [Fact]
    public void EmptyFlow_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new StepFlow("Test", []));
    }

    [Fact]
    public void ForCreate_Plain_HasNoLoaderWorldOrPack()
    {
        var flow = StepLists.ForCreate(installsLoader: false, importsWorld: false, installsWorldMods: false, installsModpack: false);

        Assert.Equal(["Version", "Java", "ServerFiles", "Settings", "Ready"], flow.Steps);
    }

    [Fact]
    public void ForCreate_ForgeWithImportedModdedWorld_AddsLoaderWorldAndMods()
    {
        var flow = StepLists.ForCreate(installsLoader: true, importsWorld: true, installsWorldMods: true, installsModpack: false);

        Assert.Equal(["Version", "Java", "ServerFiles", "Loader", "Settings", "CopyWorld", "WorldMods", "Ready"], flow.Steps);
    }

    [Fact]
    public void ForCreate_Modpack_AddsPackStep()
    {
        var flow = StepLists.ForCreate(installsLoader: false, importsWorld: false, installsWorldMods: false, installsModpack: true);

        Assert.Contains("Modpack", flow.Steps);
        Assert.DoesNotContain("CopyWorld", flow.Steps);
    }

    [Theory]
    [InlineData(CreateServerStage.ReadingVersion, "Version")]
    [InlineData(CreateServerStage.DownloadingJava, "Java")]
    [InlineData(CreateServerStage.DownloadingServer, "ServerFiles")]
    [InlineData(CreateServerStage.InstallingLoader, "Loader")]
    [InlineData(CreateServerStage.WritingFiles, "Settings")]
    public void ForStage_MapsEveryCreateStageToACreateStep(CreateServerStage stage, string step)
    {
        Assert.Equal(step, StepLists.ForStage(stage));
        Assert.Contains(step, StepLists.ForCreate(true, false, false, false).Steps);
    }

    [Fact]
    public void ForStart_OnVps_AddsConnectAndUpload()
    {
        Assert.Equal(["Backup", "Java", "Launch", "LoadWorld", "Online"], StepLists.ForStart(onVps: false).Steps);
        Assert.Equal(["Backup", "Connect", "Java", "Upload", "Launch", "LoadWorld", "Online"], StepLists.ForStart(onVps: true).Steps);
    }

    [Fact]
    public void ForStart_FolderKeptOnVps_SkipsBackupAndUpload()
    {
        Assert.Equal(["Connect", "Java", "Launch", "LoadWorld", "Online"], StepLists.ForStart(onVps: true, usesVpsCopy: true).Steps);
    }

    [Fact]
    public void ForStop_BringsTheFolderBackOnlyWhenAsked()
    {
        Assert.DoesNotContain("BringBack", StepLists.ForStop(bringsFolderBack: false).Steps);
        Assert.Contains("BringBack", StepLists.ForStop(bringsFolderBack: true).Steps);
    }

    [Fact]
    public void ForBackup_RunningServer_FlushesFirst()
    {
        Assert.Equal(["Flush", "Pack", "Saved"], StepLists.ForBackup(serverRunning: true).Steps);
        Assert.Equal(["Pack", "Saved"], StepLists.ForBackup(serverRunning: false).Steps);
    }

    [Theory]
    [InlineData("[12:00:00] [Server thread/INFO]: Preparing level \"world\"", ServerPhase.LoadingWorld)]
    [InlineData("[12:00:00 INFO]: Preparing start region for dimension minecraft:overworld", ServerPhase.LoadingWorld)]
    [InlineData("[12:00:01] [Worker-Main-2/INFO]: Preparing spawn area: 42%", ServerPhase.LoadingWorld)]
    [InlineData("[12:10:00] [Server thread/INFO]: Saving worlds", ServerPhase.SavingWorld)]
    [InlineData("[12:10:00 INFO]: Saving chunks for level 'ServerLevel[world]'/minecraft:overworld", ServerPhase.SavingWorld)]
    [InlineData("[12:10:01] [Server thread/INFO]: ThreadedAnvilChunkStorage: All dimensions are saved", ServerPhase.WorldSaved)]
    [InlineData("[12:10:01] [Server thread/INFO]: ThreadedAnvilChunkStorage (world): All chunks are saved", ServerPhase.WorldSaved)]
    public void Phase_KnownLines(string line, ServerPhase expected)
    {
        Assert.Equal(expected, ServerLogParser.Phase(line));
    }

    [Theory]
    [InlineData("[12:00:00] [Server thread/INFO]: Starting minecraft server version 1.21.4")]
    [InlineData("[12:05:00] [Server thread/INFO]: <Steve> Saving worlds")]
    [InlineData("Preparing level \"world\"")]
    public void Phase_OtherLinesAndChat_ReturnNull(string line)
    {
        Assert.Null(ServerLogParser.Phase(line));
    }

    private static StepState[] States(StepFlow flow) => [.. Enumerable.Range(0, flow.Steps.Count).Select(flow.StateOf)];
}
