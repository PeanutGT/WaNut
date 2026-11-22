using System.IO.Pipes;

namespace WaNut.Core.Hosting;

/// <summary>
/// Lets only one WaNut run per Windows session. A second start does not open a second window: it asks the first one
/// to come forward and exits. The mutex decides who is first; a named pipe carries the "come forward" request.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly string _pipeName;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;

    private SingleInstanceGuard(string name, Mutex mutex)
    {
        _pipeName = name + ".activate";
        _mutex = mutex;
    }

    /// <summary>Raised on a background thread when another start asked this one to come forward.</summary>
    public event EventHandler? ActivationRequested;

    /// <summary>
    /// The guard when this is the first WaNut, or null when another one already runs and has been asked to come forward.
    /// </summary>
    public static SingleInstanceGuard? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: false, name);
        bool owned;
        try
        {
            owned = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // The last WaNut died without letting go; this one is now the owner.
            owned = true;
        }

        if (owned)
        {
            return new SingleInstanceGuard(name, mutex);
        }

        mutex.Dispose();
        SignalFirst(name + ".activate");
        return null;
    }

    /// <summary>Starts listening for later starts. Call once the window can be shown.</summary>
    public void StartListening() => _listener ??= Task.Run(ListenAsync);

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _listener?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // The listener only ends by cancellation.
        }

        _stop.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from another thread than the one that took it; closing the handle frees it anyway.
        }

        _mutex.Dispose();
    }

    private static void SignalFirst(string pipeName)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);

            // The first WaNut may still be opening its window; give it a moment to start listening.
            client.Connect(3000);
            client.WriteByte(1);
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            // Nothing listening: the first WaNut is still starting or stuck. This one just exits.
        }
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                var buffer = new byte[1];
                if (await server.ReadAsync(buffer, _stop.Token).ConfigureAwait(false) > 0)
                {
                    ActivationRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A client that left early; wait for the next one.
            }
        }
    }
}
