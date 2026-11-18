using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace WaNut.Core.Remote.Files;

/// <summary>
/// One open connection to a VPS for browsing and editing its files: an SSH channel for commands and an SFTP channel
/// for short file operations, both kept alive and opened again once when the connection drops. SFTP calls take turns,
/// so a background job never interleaves with an edit; long transfers get a connection of their own
/// (<see cref="TransferAsync{T}"/>) so they don't hold edits up.
/// </summary>
public sealed class VpsSession(VpsConnector connector, VpsProfile profile) : IAsyncDisposable
{
    /// <summary>Often enough that NAT routers and firewalls don't drop a connection left open while the user reads.</summary>
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _sftpGate = new(1, 1);
    private SshClient? _ssh;
    private SftpClient? _sftp;
    private bool _disposed;

    public VpsProfile Profile => profile;

    /// <summary>
    /// Runs <paramref name="use"/> with a client from <paramref name="get"/>; when the connection turns out to be lost,
    /// <paramref name="drop"/> forgets that client and the call is made once more with a fresh one.
    /// </summary>
    public static async Task<T> RetryOnceAsync<TClient, T>(Func<Task<TClient>> get, Func<TClient, Task<T>> use, Action<TClient> drop)
    {
        var client = await get().ConfigureAwait(false);
        try
        {
            return await use(client).ConfigureAwait(false);
        }
        catch (SshConnectionException)
        {
            drop(client);
        }

        return await use(await get().ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="command"/>, with <paramref name="input"/> as its standard input when given, so data never goes through the shell.</summary>
    public Task<RemoteCommandResult> RunAsync(string command, string? input = null, CancellationToken cancellationToken = default) =>
        RetryOnceAsync(
            () => GetAsync(() => _ssh, c => _ssh = c, () => connector.ConnectAsync(profile, null, cancellationToken), cancellationToken),
            ssh => ExecuteAsync(ssh, command, input, cancellationToken),
            ssh => Drop(ref _ssh, ssh));

    /// <summary>Uses the shared SFTP channel, one call at a time.</summary>
    public Task<T> SftpAsync<T>(Func<SftpClient, Task<T>> use, CancellationToken cancellationToken = default) =>
        RetryOnceAsync(
            () => GetAsync(() => _sftp, c => _sftp = c, () => connector.ConnectSftpAsync(profile, cancellationToken), cancellationToken),
            async sftp =>
            {
                await _sftpGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await use(sftp).ConfigureAwait(false);
                }
                finally
                {
                    _sftpGate.Release();
                }
            },
            sftp => Drop(ref _sftp, sftp));

    /// <summary>Uses an SFTP connection of its own for a long upload or download, closed afterwards.</summary>
    public Task<T> TransferAsync<T>(Func<SftpClient, Task<T>> use, CancellationToken cancellationToken = default) =>
        RetryOnceAsync(
            async () => KeepAlive(await connector.ConnectSftpAsync(profile, cancellationToken).ConfigureAwait(false)),
            async sftp =>
            {
                using (sftp)
                {
                    return await use(sftp).ConfigureAwait(false);
                }
            },
            _ => { });

    public async ValueTask DisposeAsync()
    {
        await _connectGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            Close(_ssh);
            Close(_sftp);
            _ssh = null;
            _sftp = null;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private static async Task<RemoteCommandResult> ExecuteAsync(SshClient ssh, string command, string? input, CancellationToken cancellationToken)
    {
        using var run = ssh.CreateCommand(command, Encoding.UTF8);
        var running = run.ExecuteAsync(cancellationToken);
        if (input is not null)
        {
            await using var stdin = run.CreateInputStream();
            await stdin.WriteAsync(Encoding.UTF8.GetBytes(input), cancellationToken).ConfigureAwait(false);
        }

        await running.ConfigureAwait(false);
        return new RemoteCommandResult(run.ExitStatus ?? -1, run.Result, run.Error);
    }

    /// <summary>The connected client in a slot, or a fresh one put there. <paramref name="read"/> and <paramref name="write"/> name the slot.</summary>
    private async Task<TClient> GetAsync<TClient>(Func<TClient?> read, Action<TClient?> write, Func<Task<TClient>> connect, CancellationToken cancellationToken)
        where TClient : BaseClient
    {
        if (read() is { IsConnected: true } connected)
        {
            return connected;
        }

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (read() is { IsConnected: true } current)
            {
                return current;
            }

            Close(read());
            write(null);
            var fresh = KeepAlive(await connect().ConfigureAwait(false));
            write(fresh);
            return fresh;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private static TClient KeepAlive<TClient>(TClient client)
        where TClient : BaseClient
    {
        client.KeepAliveInterval = KeepAliveInterval;
        return client;
    }

    private static void Drop<TClient>(ref TClient? slot, TClient client)
        where TClient : BaseClient
    {
        if (Interlocked.CompareExchange(ref slot, null, client) == client)
        {
            Close(client);
        }
    }

    private static void Close(BaseClient? client)
    {
        try
        {
            client?.Dispose();
        }
        catch (Exception e) when (e is SshException or ObjectDisposedException or InvalidOperationException)
        {
            // The connection is already gone; nothing else to let go of.
        }
    }
}
