using WaNut.Core.Remote.Files;

namespace WaNut.Core.Hotplug;

/// <summary>
/// The lock file in the VPS staging folder, held by one hot-plug at a time. Holds a process id and a time stamp so a
/// second administrator can see who has it; a lock nobody refreshed for <see cref="StaleAfter"/> is taken over.
/// </summary>
public sealed class HotplugLock : IAsyncDisposable
{
    /// <summary>A hot-plug that is still working refreshes the lock at least this often.</summary>
    public static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromMinutes(10);

    private readonly IRemoteCommandRunner _runner;
    private readonly string _root;
    private readonly string _ownerLine;
    private bool _released;

    private HotplugLock(IRemoteCommandRunner runner, string root, string lockFile, string ownerLine)
    {
        _runner = runner;
        _root = root;
        LockFile = lockFile;
        _ownerLine = ownerLine;
    }

    public string LockFile { get; }

    /// <summary>Takes the lock, or says who has it.</summary>
    /// <param name="root">The server folder's real path on the VPS.</param>
    /// <param name="staleAfter">How long an unrefreshed lock is respected; <see cref="DefaultStaleAfter"/> when null.</param>
    public static async Task<HotplugLockResult> TryAcquireAsync(
        IRemoteCommandRunner runner,
        string root,
        HotplugWorkspace workspace,
        TimeSpan? staleAfter = null,
        CancellationToken cancellationToken = default)
    {
        var owner = $"owner={Guid.NewGuid():N}";
        var lines = new[]
        {
            $"pid={Environment.ProcessId}",
            $"machine={Clean(Environment.MachineName)}",
            owner,
        };
        var seconds = (int)Math.Max(1, (staleAfter ?? DefaultStaleAfter).TotalSeconds);
        var result = await runner.RunAsync(HotplugCommands.AcquireLock(root, workspace.LockFile, lines, seconds), cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ExitStatus switch
        {
            0 => new HotplugLockResult(new HotplugLock(runner, root, workspace.LockFile, owner), null),
            HotplugCommands.LockHeldExit => new HotplugLockResult(null, HotplugLockHolder.Parse(result.Output)),
            _ => throw new InvalidOperationException($"Taking the hot-plug lock on the VPS failed ({result.ExitStatus}): {result.Error.Trim()}"),
        };
    }

    /// <summary>Marks the lock as still in use. Returns false when it was taken over meanwhile.</summary>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(HotplugCommands.RefreshLock(_root, LockFile, _ownerLine), cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ExitStatus switch
        {
            0 => true,
            HotplugCommands.LockHeldExit => false,
            _ => throw new InvalidOperationException($"Refreshing the hot-plug lock on the VPS failed ({result.ExitStatus}): {result.Error.Trim()}"),
        };
    }

    /// <summary>Gives the lock back. A lock someone else took over meanwhile stays theirs.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        try
        {
            await _runner.RunAsync(HotplugCommands.ReleaseLock(_root, LockFile, _ownerLine)).ConfigureAwait(false);
        }
        catch (Exception e) when (e is Renci.SshNet.Common.SshException or IOException or ObjectDisposedException)
        {
            // The connection is gone; the lock times out by itself.
        }
    }

    private static string Clean(string value) => new(value.Where(c => !char.IsControl(c)).ToArray());
}
