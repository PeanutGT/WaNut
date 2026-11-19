using System.Globalization;
using WaNut.Core.Hosting;

namespace WaNut.Core.Hotplug.Safety;

/// <summary>
/// Holds a hot-plug back while the server is busy: when a tick takes longer than <see cref="MsptLimit"/> the user is
/// asked to go ahead anyway, cancel, or queue it until fewer than <see cref="QueueBelowPlayers"/> players are online
/// (checked every <see cref="PollInterval"/>). A server that can't tell its tick time goes ahead without asking.
/// </summary>
/// <param name="ask">Asks the user what to do with a busy server.</param>
/// <param name="queued">Told the latest reading while the hot-plug waits in the queue.</param>
public sealed class LoadReloadGate(
    Func<ServerLoad, CancellationToken, Task<LoadChoice>> ask,
    Action<ServerLoad>? queued = null,
    TimeProvider? time = null) : IReloadGate
{
    public const double DefaultMsptLimit = 45;

    public const int DefaultQueueBelowPlayers = 2;

    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Above this many milliseconds per tick a /reload is likely to make players stutter.</summary>
    public double MsptLimit { get; init; } = DefaultMsptLimit;

    /// <summary>A queued hot-plug runs once fewer than this many players are online.</summary>
    public int QueueBelowPlayers { get; init; } = DefaultQueueBelowPlayers;

    public TimeSpan PollInterval { get; init; } = DefaultPollInterval;

    public TimeSpan QueryTimeout { get; init; } = ServerLoadProbe.DefaultTimeout;

    public async Task<string?> CheckAsync(IServerConsole console, CancellationToken cancellationToken)
    {
        var load = await ServerLoadProbe.ReadAsync(console, QueryTimeout, cancellationToken).ConfigureAwait(false);
        if (!IsBusy(load))
        {
            return null;
        }

        switch (await ask(load, cancellationToken).ConfigureAwait(false))
        {
            case LoadChoice.RunNow:
                return null;
            case LoadChoice.Cancel:
                return $"Held back because the server is busy ({load.Mspt!.Value.ToString("0.#", CultureInfo.InvariantCulture)} ms per tick).";
        }

        while (true)
        {
            queued?.Invoke(load);
            await Task.Delay(PollInterval, _time, cancellationToken).ConfigureAwait(false);
            load = await ServerLoadProbe.ReadAsync(console, QueryTimeout, cancellationToken).ConfigureAwait(false);
            if (IsQuiet(load))
            {
                return null;
            }
        }
    }

    private bool IsBusy(ServerLoad load) => load.Mspt > MsptLimit;

    /// <summary>Few enough players; when the player count can't be read, a tick time back under the limit will do.</summary>
    private bool IsQuiet(ServerLoad load) => load.Players is { } players ? players < QueueBelowPlayers : !IsBusy(load);
}
