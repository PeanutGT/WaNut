using System.Net.Sockets;

namespace WaNut.Bridge.Rcon;

/// <summary>
/// Keeps an RCON connection to the server up for as long as the bridge runs. The server is often off or still
/// starting, so a failed connect waits 1, 2, 4 ... up to 60 seconds before the next try; <see cref="Wake"/> skips the
/// wait when the log shows the server has just started.
/// </summary>
public sealed class RconLink(
    Func<CancellationToken, Task<RconClient>> connect,
    BridgeLog log,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IRconCommands
{
    public static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan MaxRetry = TimeSpan.FromSeconds(60);

    private readonly Backoff _backoff = new(FirstRetry, MaxRetry);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly object _gate = new();
    private RconClient? _client;
    private TaskCompletionSource _broken = NewSignal();
    private CancellationTokenSource _wait = new();
    private bool _authFailureLogged;

    public event Action<bool>? ConnectionChanged;

    public bool IsConnected => Volatile.Read(ref _client) is not null;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await connect(cancellationToken).ConfigureAwait(false);
                lock (_gate)
                {
                    _client = client;
                    _broken = NewSignal();
                }

                _backoff.Reset();
                _authFailureLogged = false;
                log.Info("RCON connected.");
                ConnectionChanged?.Invoke(true);
                await _broken.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (RconAuthenticationException)
            {
                // Logged once: until someone fixes the password every retry fails the same way.
                if (!_authFailureLogged)
                {
                    log.Error("RCON refused the password. Check rcon.password in server.properties and bridge.config.json.");
                    _authFailureLogged = true;
                }
            }
            catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException or OperationCanceledException)
            {
                // The server is off or restarting; nothing to report on every try.
            }

            await DropAsync().ConfigureAwait(false);
            await WaitBeforeRetryAsync(cancellationToken).ConfigureAwait(false);
        }

        await DropAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Tries to connect now instead of after the current wait. Called when the log shows the server has just started:
    /// a connection that is still open then belongs to the server that died, so it is dropped and made again rather
    /// than losing the first command to it.
    /// </summary>
    public void Wake()
    {
        lock (_gate)
        {
            _backoff.Reset();
            _wait.Cancel();
            if (_client is not null)
            {
                _broken.TrySetResult();
            }
        }
    }

    public async Task<string?> TryExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        var client = Volatile.Read(ref _client);
        if (client is null)
        {
            return null;
        }

        try
        {
            return await client.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            // A timed-out or broken connection may be half way through a packet; start a fresh one.
            log.Warn("RCON connection lost.");
            lock (_gate)
            {
                _broken.TrySetResult();
            }

            return null;
        }
    }

    private async Task DropAsync()
    {
        RconClient? client;
        lock (_gate)
        {
            client = _client;
            _client = null;
        }

        if (client is not null)
        {
            ConnectionChanged?.Invoke(false);
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task WaitBeforeRetryAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource wait;
        TimeSpan time;
        lock (_gate)
        {
            if (_wait.IsCancellationRequested)
            {
                // Woken while connecting: try again at once.
                _wait.Dispose();
                _wait = new CancellationTokenSource();
                return;
            }

            wait = _wait;
            time = _backoff.Next();
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, wait.Token);
        try
        {
            await _delay(time, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Woken early.
        }
        catch (OperationCanceledException)
        {
            // Shutting down; the loop condition ends it.
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
