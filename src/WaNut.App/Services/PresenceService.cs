using System.Globalization;
using System.Windows.Threading;
using WaNut.App.Resources;
using WaNut.App.ViewModels;
using WaNut.Core.Presence;

namespace WaNut.App.Services;

/// <summary>
/// Keeps the person's Discord profile in step with the server they run: every <see cref="Interval"/> it looks at the
/// online servers (this PC or a VPS) and sets or clears the activity when the text changed. Off unless chosen in App
/// settings. Discord closed or unreachable is not an error, it is tried again next time.
/// </summary>
public sealed class PresenceService(AppSettingsViewModel app) : IDisposable
{
    /// <summary>Discord allows one activity change every 15 seconds.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    /// <summary>The Discord application that names the activity ("Playing WaNut").</summary>
    public const string ApplicationId = "1572579811258409104";

    private readonly DiscordIpcClient _client = new(ApplicationId);
    private readonly Dictionary<ServerViewModel, DateTimeOffset> _onlineSince = [];
    private DispatcherTimer? _timer;
    private Func<IReadOnlyList<ServerViewModel>>? _servers;
    private string? _shown;
    private bool _busy;

    public void Start(Func<IReadOnlyList<ServerViewModel>> servers)
    {
        _servers = servers;
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += async (_, _) => await UpdateAsync();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer?.Stop();
        _client.Dispose();
    }

    private async Task UpdateAsync()
    {
        if (_busy || _servers is null)
        {
            return;
        }

        _busy = true;
        try
        {
            var activity = app.Discord.ShowPresence ? Pick(_servers()) : null;
            var key = activity is null ? string.Empty : activity.Details + "|" + activity.State;
            if (key == _shown && _client.IsConnected)
            {
                return;
            }

            // Nothing to clear if it was never set.
            if (activity is null && _shown is null)
            {
                return;
            }

            if (await _client.SetActivityAsync(activity, CancellationToken.None))
            {
                _shown = activity is null ? null : key;
            }
            else
            {
                // Discord is closed; show it again as soon as it opens.
                _shown = null;
            }
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>The online server with the most players; null when none is online.</summary>
    private PresenceActivity? Pick(IReadOnlyList<ServerViewModel> servers)
    {
        var online = servers.Where(s => s.IsOnline).ToList();
        foreach (var gone in _onlineSince.Keys.Where(k => !online.Contains(k)).ToList())
        {
            _onlineSince.Remove(gone);
        }

        var best = online.OrderByDescending(s => s.Players.Count).FirstOrDefault();
        if (best is null)
        {
            return null;
        }

        if (!_onlineSince.TryGetValue(best, out var since))
        {
            _onlineSince[best] = since = DateTimeOffset.UtcNow;
        }

        var key = best.RunsOnVps ? "Presence_StateVps" : "Presence_State";
        var state = string.Format(CultureInfo.CurrentCulture, Text.Get(key), best.TypeAndVersionText, best.Players.Count);
        return new PresenceActivity(best.Name, state, since);
    }
}
