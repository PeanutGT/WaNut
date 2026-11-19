using WaNut.Core.Hosting;
using WaNut.Core.Hotplug;
using WaNut.Core.Hotplug.Safety;
using WaNut.Core.Remote.Files;
using WaNut.Core.Remote.Run;
using WaNut.Core.Storage;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Hotplug;

/// <summary>
/// The whole hot-plug against a server folder on this machine: the real sh swap commands (through
/// <see cref="LocalShRunner"/>), real files and a console that answers like Minecraft. Needs a POSIX sh, so it
/// carries Category=Ssh like the other sh tests and Windows CI skips it.
/// </summary>
[Trait("Category", "Ssh")]
public sealed class DatapackHotplugOrchestratorTests : IAsyncLifetime
{
    private readonly TempDir _dir = new();
    private readonly LocalShRunner _sh = new();
    private string _root = null!;
    private string _datapacks = null!;
    private FakeDatapackConsole _console = null!;
    private HotplugConnection _connection = null!;

    public async Task InitializeAsync()
    {
        var root = Path.Combine(_dir.Path, "srv");
        Directory.CreateDirectory(Path.Combine(root, "world", "datapacks"));
        File.WriteAllText(Path.Combine(root, "server.properties"), "level-name=world\n");
        _root = (await _sh.RunAsync($"cd {RemoteShell.Quote(root)} && pwd -P")).Output.Trim();
        _datapacks = Path.Combine(_root, "world", "datapacks");
        _console = new FakeDatapackConsole(_datapacks);
        _connection = new HotplugConnection(_sh, new LocalFileSystemAdapter(_root), _console);
    }

    public Task DisposeAsync()
    {
        _dir.Dispose();
        return Task.CompletedTask;
    }

    private string Local(string name, params string[] entries) =>
        PackZip.Make(Path.Combine(_dir.Path, "local", Guid.NewGuid().ToString("N"), name), PackZip.Meta, entries.Length == 0 ? ["data/ev/loot_table/a.json"] : entries);

    private DatapackHotplugOrchestrator Orchestrator(HotplugHooks? hooks = null) => new(_connection, hooks);

    private static HotplugRequest Add(params string[] paths) => new(paths, [], "1.21.1");

    private string[] Backups() =>
        Directory.Exists(Path.Combine(_root, ".nut-backup", "datapacks")) ? Directory.GetFiles(Path.Combine(_root, ".nut-backup", "datapacks")).Select(Path.GetFileName).OfType<string>().ToArray() : [];

    private string[] Staging() =>
        Directory.GetFiles(Path.Combine(_root, ".nut-staging", "datapacks")).Select(Path.GetFileName).OfType<string>().ToArray();

    [Fact]
    public async Task RunAsync_NewPack_AddedEnabledAndAudited()
    {
        var states = new List<HotplugState>();
        var report = await Orchestrator().RunAsync(Add(Local("loot.zip")), new SyncList(states));

        Assert.Equal(HotplugState.Completed, report.Outcome);
        Assert.Equal([new HotplugItem("loot.zip", HotplugSwapResult.Added, report.Inspections[0].Sha256)], report.Items);
        Assert.True(File.Exists(Path.Combine(_datapacks, "loot.zip")));
        Assert.Contains("loot.zip", _console.Enabled);
        Assert.Equal(["reload", "datapack list enabled"], _console.Commands);
        Assert.Empty(Staging());
        Assert.Empty(Backups());
        Assert.False(File.Exists(Path.Combine(_root, ".nut-staging", ".lock")));
        Assert.Contains("\"outcome\":\"Completed\"", File.ReadAllText(Path.Combine(_root, ".nut-backup", "hotplug-audit.log")));
        Assert.Equal(
            [HotplugState.PreFlightCheck, HotplugState.UploadingStaging, HotplugState.VerifyingRemote, HotplugState.AtomicSwap, HotplugState.ApplyingAndReloading, HotplugState.Completed],
            states.Distinct());
    }

    [Fact]
    public async Task RunAsync_Update_OldFileKeptAsBackup()
    {
        var target = Path.Combine(_datapacks, "loot.zip");
        File.WriteAllText(target, "old version");
        _console.Enabled.Add("loot.zip");
        var update = Local("loot.zip");

        var report = await Orchestrator().RunAsync(Add(update));

        Assert.Equal(HotplugState.Completed, report.Outcome);
        Assert.Equal(HotplugSwapResult.Replaced, report.Items[0].Action);
        Assert.Equal(File.ReadAllBytes(update), File.ReadAllBytes(target));
        var backup = Assert.Single(Backups(), n => n.StartsWith("loot.", StringComparison.Ordinal) && n.EndsWith(".zip", StringComparison.Ordinal));
        Assert.Equal("old version", File.ReadAllText(Path.Combine(_root, ".nut-backup", "datapacks", backup)));
    }

    [Fact]
    public async Task RunAsync_UpdateOfDisabledPack_StaysDisabled()
    {
        File.WriteAllText(Path.Combine(_datapacks, "off.zip"), "old");
        _console.Disabled.Add("off.zip");

        var report = await Orchestrator().RunAsync(Add(Local("off.zip")));

        Assert.Equal(HotplugState.Completed, report.Outcome);
        Assert.DoesNotContain("off.zip", _console.Enabled);
        Assert.DoesNotContain(_console.Commands, c => c.StartsWith("datapack enable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_NewPackDisabledBefore_EnabledExplicitly()
    {
        _console.Disabled.Add("again.zip");

        var report = await Orchestrator().RunAsync(Add(Local("again.zip")));

        Assert.Equal(HotplugState.Completed, report.Outcome);
        Assert.Contains("datapack enable \"file/again.zip\"", _console.Commands);
        Assert.Contains("again.zip", _console.Enabled);
    }

    [Fact]
    public async Task RunAsync_ServerWontEnable_RolledBackToOldFile()
    {
        var target = Path.Combine(_datapacks, "loot.zip");
        File.WriteAllText(target, "old version");
        _console.Disabled.Add("new.zip");
        _console.Refuse.Add("new.zip");

        var report = await Orchestrator().RunAsync(Add(Local("loot.zip"), Local("new.zip")));

        Assert.Equal(HotplugState.RolledBack, report.Outcome);
        Assert.Contains("new.zip", report.Message);
        Assert.Equal("old version", File.ReadAllText(target));
        Assert.False(File.Exists(Path.Combine(_datapacks, "new.zip")));
        Assert.Equal("reload", _console.Commands[^1]);
        Assert.Empty(Staging());
    }

    [Fact]
    public async Task RunAsync_SentinelReportsFailure_RolledBack()
    {
        var hooks = new HotplugHooks(Sentinel: new FailingSentinel("Couldn't parse data file"));

        var report = await Orchestrator(hooks).RunAsync(Add(Local("broken.zip")));

        Assert.Equal(HotplugState.RolledBack, report.Outcome);
        Assert.StartsWith("Couldn't parse data file", report.Message);
        Assert.Equal(["Couldn't parse data file"], report.ConsoleLines);
        Assert.Empty(Directory.GetFiles(_datapacks));
        Assert.Contains("RolledBack", File.ReadAllText(Path.Combine(_root, ".nut-backup", "hotplug-audit.log")));
    }

    private static HotplugHooks LogSentinel() =>
        new(Sentinel: new ReloadLogSentinel { Window = TimeSpan.FromSeconds(1), Grace = TimeSpan.FromMilliseconds(100) });

    [Fact]
    public async Task RunAsync_BrokenLootTableUpdate_RolledBackToOldVersion()
    {
        var target = Path.Combine(_datapacks, "loot.zip");
        File.WriteAllText(target, "old version");
        _console.Enabled.Add("loot.zip");
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var report = await Orchestrator(LogSentinel()).RunAsync(Add(Local("loot.zip", $"data/ev/loot_table/{FakeDatapackConsole.BrokenMarker}.json")));

        Assert.Equal(HotplugState.RolledBack, report.Outcome);
        Assert.Equal("old version", File.ReadAllText(target));
        Assert.Contains("loot.zip", _console.Enabled);
        Assert.Contains("Couldn't parse element", report.Message);
        Assert.DoesNotContain("another pack", report.Message);
        Assert.Equal(2, report.ConsoleLines!.Count);
        Assert.Equal(2, _console.Commands.Count(c => c == "reload"));
        Assert.Contains("\"log\":[", File.ReadAllText(Path.Combine(_root, ".nut-backup", "hotplug-audit.log")));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RunAsync_BrokenNewPack_RemovedAndNotEnabled()
    {
        var report = await Orchestrator(LogSentinel()).RunAsync(Add(Local("new.zip", $"data/ev/loot_table/{FakeDatapackConsole.BrokenMarker}.json")));

        Assert.Equal(HotplugState.RolledBack, report.Outcome);
        Assert.Empty(Directory.GetFiles(_datapacks));
        Assert.Empty(Backups());
        Assert.DoesNotContain(_console.Commands, c => c.StartsWith("datapack enable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ErrorsFromAnotherPack_SaysSoAfterRollback()
    {
        File.WriteAllText(Path.Combine(_datapacks, "old.zip"), FakeDatapackConsole.BrokenMarker);
        _console.Enabled.Add("old.zip");

        var report = await Orchestrator(LogSentinel()).RunAsync(Add(Local("fine.zip")));

        Assert.Equal(HotplugState.RolledBack, report.Outcome);
        Assert.Contains("another pack", report.Message);
        Assert.False(File.Exists(Path.Combine(_datapacks, "fine.zip")));
    }

    [Fact]
    public async Task RunAsync_CleanReloadWithSentinel_Completed()
    {
        var report = await Orchestrator(LogSentinel()).RunAsync(Add(Local("fine.zip")));

        Assert.Equal(HotplugState.Completed, report.Outcome);
        Assert.Null(report.ConsoleLines);
    }

    [Fact]
    public async Task RunAsync_GateHoldsBack_NothingTouched()
    {
        var report = await Orchestrator(new HotplugHooks(Gate: new HoldingGate("busy"))).RunAsync(Add(Local("loot.zip")));

        Assert.Equal(HotplugState.Aborted, report.Outcome);
        Assert.Equal("busy", report.Message);
        Assert.Empty(_console.Commands);
        Assert.False(Directory.Exists(Path.Combine(_root, ".nut-staging")));
    }

    [Fact]
    public async Task RunAsync_Dimension_RefusedBeforeUpload()
    {
        var report = await Orchestrator().RunAsync(Add(Local("moon.zip", "data/ev/dimension/moon.json")));

        Assert.Equal(HotplugState.Aborted, report.Outcome);
        Assert.Contains("dimension", report.Message);
        Assert.Empty(_console.Commands);
        Assert.False(Directory.Exists(Path.Combine(_root, ".nut-staging")));
    }

    [Fact]
    public async Task RunAsync_ConfirmDeclined_Aborted()
    {
        var request = new HotplugRequest([Local("terrain.zip", "data/ev/worldgen/biome/b.json")], [], "1.21.1", (_, _) => Task.FromResult(false));

        var report = await Orchestrator().RunAsync(request);

        Assert.Equal(HotplugState.Aborted, report.Outcome);
        Assert.Empty(Directory.GetFiles(_datapacks));
    }

    [Fact]
    public async Task RunAsync_DamagedUpload_NothingEntersWorld()
    {
        var damaging = new HotplugConnection(_sh, new DamagingFiles(new LocalFileSystemAdapter(_root)), _console);

        var report = await new DatapackHotplugOrchestrator(damaging).RunAsync(Add(Local("loot.zip")));

        Assert.Equal(HotplugState.Aborted, report.Outcome);
        Assert.Contains("damaged", report.Message);
        Assert.Empty(Directory.GetFiles(_datapacks));
        Assert.Empty(Staging());
        Assert.Empty(_console.Commands);
    }

    [Fact]
    public async Task RunAsync_LockHeld_AbortedWithHolder()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".nut-staging", "datapacks"));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        File.WriteAllText(Path.Combine(_root, ".nut-staging", ".lock"), $"pid=42\nmachine=OTHER\nowner=x\ntime={now}\n");

        var report = await Orchestrator().RunAsync(Add(Local("loot.zip")));

        Assert.Equal(HotplugState.Aborted, report.Outcome);
        Assert.Equal("OTHER", report.Holder?.Machine);
        Assert.Empty(Directory.GetFiles(_datapacks));
    }

    [Fact]
    public async Task RunAsync_Remove_DisabledThenMovedToBackup()
    {
        File.WriteAllText(Path.Combine(_datapacks, "old.zip"), "bye");
        _console.Enabled.Add("old.zip");

        var report = await Orchestrator().RunAsync(new HotplugRequest([], ["old.zip"], "1.21.1"));

        Assert.Equal(HotplugState.Completed, report.Outcome);
        Assert.Equal(HotplugSwapResult.Retired, report.Items[0].Action);
        Assert.False(File.Exists(Path.Combine(_datapacks, "old.zip")));
        Assert.Single(Backups(), n => n.StartsWith("old.", StringComparison.Ordinal));
        Assert.Contains("datapack disable \"file/old.zip\"", _console.Commands);
        Assert.DoesNotContain("reload", _console.Commands);
    }

    [Fact]
    public async Task RunAsync_RemoveMissing_Aborted()
    {
        var report = await Orchestrator().RunAsync(new HotplugRequest([], ["ghost.zip"], "1.21.1"));
        Assert.Equal(HotplugState.Aborted, report.Outcome);
        Assert.Contains("ghost.zip", report.Message);
    }

    [Fact]
    public async Task RunAsync_TargetIsFolder_LeftAlone()
    {
        Directory.CreateDirectory(Path.Combine(_datapacks, "loot.zip"));

        var report = await Orchestrator().RunAsync(Add(Local("loot.zip")));

        Assert.Equal(HotplugState.Aborted, report.Outcome);
        Assert.True(Directory.Exists(Path.Combine(_datapacks, "loot.zip")));
        Assert.Empty(Staging());
    }

    [Fact]
    public async Task RunAsync_ConsoleSilent_CompletedWithWarning()
    {
        _console.Answers = false;
        var orchestrator = new DatapackHotplugOrchestrator(_connection) { ConsoleTimeout = TimeSpan.FromMilliseconds(200) };

        var report = await orchestrator.RunAsync(Add(Local("loot.zip")));

        Assert.Equal(HotplugState.Completed, report.Outcome);
        Assert.Contains("couldn't confirm", report.Message);
    }

    [Fact]
    public async Task RunAsync_CancelledDuringUpload_NothingChanged()
    {
        using var cts = new CancellationTokenSource();
        var cancelling = new HotplugConnection(_sh, new CancellingFiles(new LocalFileSystemAdapter(_root), cts), _console);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DatapackHotplugOrchestrator(cancelling).RunAsync(Add(Local("loot.zip")), cancellationToken: cts.Token));

        Assert.Empty(Directory.GetFiles(_datapacks));
        Assert.Empty(Staging());
        Assert.False(File.Exists(Path.Combine(_root, ".nut-staging", ".lock")));
    }

    [Fact]
    public async Task RunAsync_BatchOfThree_OneReload()
    {
        var report = await Orchestrator().RunAsync(Add(Local("a.zip"), Local("b.zip"), Local("c.zip")));

        Assert.Equal(HotplugState.Completed, report.Outcome);
        Assert.Equal(3, Directory.GetFiles(_datapacks).Length);
        Assert.Single(_console.Commands, c => c == "reload");
    }

    private sealed class SyncList(List<HotplugState> states) : IProgress<HotplugProgress>
    {
        public void Report(HotplugProgress value) => states.Add(value.State);
    }

    private sealed class FailingSentinel(string error) : IReloadSentinel, IReloadWatch
    {
        public IReloadWatch Begin(IServerConsole console) => this;

        public bool HasFailed => true;

        public Task<ReloadFailure?> FailureAsync(CancellationToken cancellationToken) => Task.FromResult<ReloadFailure?>(new ReloadFailure(error, [error]));

        public void Dispose()
        {
        }
    }

    private sealed class HoldingGate(string? reason) : IReloadGate
    {
        public Task<string?> CheckAsync(IServerConsole console, CancellationToken cancellationToken) => Task.FromResult(reason);
    }

    /// <summary>Flips one byte of every upload, like a connection that corrupts data.</summary>
    private sealed class DamagingFiles(LocalFileSystemAdapter inner) : ForwardingFiles(inner)
    {
        public override async Task UploadFileAsync(string localPath, string relativePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            await base.UploadFileAsync(localPath, relativePath, progress, cancellationToken);
            var path = Path.Combine(inner.WorkspaceRoot, relativePath);
            var bytes = File.ReadAllBytes(path);
            bytes[^1] ^= 0xFF;
            File.WriteAllBytes(path, bytes);
        }
    }

    /// <summary>Cancels the run once the upload has started.</summary>
    private sealed class CancellingFiles(LocalFileSystemAdapter inner, CancellationTokenSource cts) : ForwardingFiles(inner)
    {
        public override async Task UploadFileAsync(string localPath, string relativePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            await base.UploadFileAsync(localPath, relativePath, progress, CancellationToken.None);
            await cts.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private abstract class ForwardingFiles(IServerFileSystem inner) : IServerFileSystem
    {
        public bool IsRemote => inner.IsRemote;

        public string WorkspaceRoot => inner.WorkspaceRoot;

        public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default) => inner.ExistsAsync(relativePath, cancellationToken);

        public Task<IReadOnlyList<ServerFileNode>> ListDirectoryAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default) =>
            inner.ListDirectoryAsync(relativePath, recursive, cancellationToken);

        public Task<string> ReadAllTextAsync(string relativePath, CancellationToken cancellationToken = default) => inner.ReadAllTextAsync(relativePath, cancellationToken);

        public Task WriteAllTextAsync(string relativePath, string content, CancellationToken cancellationToken = default) => inner.WriteAllTextAsync(relativePath, content, cancellationToken);

        public virtual Task UploadFileAsync(string localPath, string relativePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            inner.UploadFileAsync(localPath, relativePath, progress, cancellationToken);

        public Task DownloadFileAsync(string relativePath, string localPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            inner.DownloadFileAsync(relativePath, localPath, progress, cancellationToken);

        public Task DeleteBatchAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default) => inner.DeleteBatchAsync(relativePaths, cancellationToken);

        public Task<string> CreateArchiveAsync(IEnumerable<string> targetRelativePaths, string outputArchiveRelativePath, CancellationToken cancellationToken = default) =>
            inner.CreateArchiveAsync(targetRelativePaths, outputArchiveRelativePath, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
