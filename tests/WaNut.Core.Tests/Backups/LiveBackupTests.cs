using WaNut.Core.Backups;
using WaNut.Core.Hosting;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Backups;

public sealed class LiveBackupTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly BackupStore _store = new(new FakeClock(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)));

    public LiveBackupTests()
    {
        var level = Path.Combine(_temp.Path, "world");
        Directory.CreateDirectory(level);
        File.WriteAllText(Path.Combine(level, "level.dat"), "v1");
    }

    public void Dispose() => _temp.Dispose();

    /// <summary>Answers "save-all flush" the way a server does, unless told to stay quiet.</summary>
    private sealed class FakeConsole(bool confirmSave = true, bool exited = false) : IServerConsole
    {
        public List<string> Commands { get; } = [];

        public event Action<string>? OutputReceived;

        public Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
        {
            if (exited)
            {
                throw new InvalidOperationException("The server is not running.");
            }

            Commands.Add(command);
            if (command == "save-all flush" && confirmSave)
            {
                OutputReceived?.Invoke("[12:00:00] [Server thread/INFO]: Saving the game (this may take a moment!)");
                OutputReceived?.Invoke("[12:00:01] [Server thread/INFO]: Saved the game");
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task CreateAsync_PausesSavingAroundTheZip()
    {
        var console = new FakeConsole();

        var backup = await LiveBackup.CreateAsync(_store, _temp.Path, console, BackupReason.Scheduled);

        Assert.NotNull(backup);
        Assert.Equal(BackupReason.Scheduled, backup.Reason);
        Assert.Equal(["save-off", "save-all flush", "save-on"], console.Commands);
    }

    [Fact]
    public async Task CreateAsync_SaveNotConfirmed_ThrowsAndTurnsSavingBackOn()
    {
        var console = new FakeConsole(confirmSave: false);

        var error = await Assert.ThrowsAsync<BackupException>(() =>
            LiveBackup.CreateAsync(_store, _temp.Path, console, BackupReason.Scheduled, saveTimeout: TimeSpan.FromMilliseconds(50)));

        Assert.Equal(BackupProblem.SaveNotConfirmed, error.Problem);
        Assert.Equal("save-on", console.Commands[^1]);
        Assert.Empty(_store.List(_temp.Path));
    }

    [Fact]
    public async Task CreateAsync_ServerGone_ThrowsWithoutTakingACopy()
    {
        var console = new FakeConsole(exited: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => LiveBackup.CreateAsync(_store, _temp.Path, console, BackupReason.Manual));

        Assert.Empty(_store.List(_temp.Path));
    }
}
