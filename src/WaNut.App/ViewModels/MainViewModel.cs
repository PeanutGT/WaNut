using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Backups;
using WaNut.Core.Instances;
using WaNut.Core.Mods;
using WaNut.Core.Network;
using WaNut.Core.Onboarding;
using WaNut.Core.Settings;

namespace WaNut.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly InstanceStore _store;
    private readonly IDialogService _dialogs;
    private readonly ServerController _controller;
    private readonly BackupStore _backups;
    private readonly FirewallService _firewall;
    private readonly PortForwardService _portForwards;
    private readonly TunnelService _tunnels;
    private readonly IClipboardService _clipboard;
    private readonly ModLibrary _mods;
    private readonly DiscordNotifier _discord;
    private readonly PlayerFaces _faces;

    public MainViewModel(
        InstanceStore store,
        IDialogService dialogs,
        ServerController controller,
        AppSettingsViewModel appSettings,
        BackupStore backups,
        FirewallService firewall,
        PortForwardService portForwards,
        TunnelService tunnels,
        IClipboardService clipboard,
        UpdateViewModel updates,
        ModLibrary mods,
        DiscordNotifier discord,
        BackupCopyService backupCopies,
        GuideViewModel guide,
        PlayerFaces faces)
    {
        _faces = faces;
        Guide = guide;
        appSettings.GuideRequested += (_, _) => StartGuide(GuideViewModel.Everything(Servers.Count > 0));
        backupCopies.CopyFailed += OnBackupCopyFailed;
        _discord = discord;
        _mods = mods;
        _firewall = firewall;
        _portForwards = portForwards;
        _tunnels = tunnels;
        _clipboard = clipboard;
        Updates = updates;
        _store = store;
        _dialogs = dialogs;
        _controller = controller;
        _backups = backups;
        AppSettings = appSettings;
        Reload();
    }

    public ObservableCollection<ServerViewModel> Servers { get; } = [];

    public AppSettingsViewModel AppSettings { get; }

    public UpdateViewModel Updates { get; }

    public GuideViewModel Guide { get; }

    [ObservableProperty]
    private ServerViewModel? _selectedServer;

    [ObservableProperty]
    private bool _isShuttingDown;

    public bool HasRunningServer => _controller.HasRunningServer;

    /// <summary>"Survival, Creative": every running server, for the close question.</summary>
    public string RunningServerNames => string.Join(Text.Get("Main_NameSeparator"), _controller.RunningServers.Select(s => s.Name));

    /// <summary>Stops every running server so their worlds are saved before WaNut closes.</summary>
    public async Task ShutdownAsync()
    {
        IsShuttingDown = true;
        try
        {
            await _controller.ShutdownAsync();
        }
        finally
        {
            IsShuttingDown = false;
        }
    }

    /// <summary>Runs whatever part of the tour is still due, such as the tabs once the first server exists.</summary>
    public void StartPendingGuide() => StartGuide(Guide.Pending(Servers.Count > 0));

    private void StartGuide(IReadOnlyList<GuideStop> stops)
    {
        // The tab stops point at the selected server's header and tabs.
        if (GuideTour.NeedsServer(stops) && SelectedServer is null)
        {
            SelectedServer = Servers.FirstOrDefault();
        }

        Guide.Start(stops);
    }

    [RelayCommand]
    private void OpenAppSettings() => _dialogs.ShowAppSettings();

    [RelayCommand]
    private void CreateServer()
    {
        var created = _dialogs.ShowCreateServer();
        if (created is null)
        {
            return;
        }

        Reload();
        SelectedServer = Servers.FirstOrDefault(s => s.Id == created.Id);
        GiveOwnPort(SelectedServer);
        StartPendingGuide();
    }

    /// <summary>
    /// A new server or copy gets a port no other server has, so they can run side by side (decision 5). Starting
    /// still moves a server whose port is taken, so a failure here only costs a later move.
    /// </summary>
    private void GiveOwnPort(ServerViewModel? server)
    {
        if (server is null)
        {
            return;
        }

        try
        {
            var settings = ServerSettingsStore.Load(server.InstanceDir);
            var taken = Servers.Where(s => s != server).Select(s => ServerSettingsStore.Load(s.InstanceDir).Port).ToHashSet();
            if (ServerPorts.Pick(settings.Port, taken, _ => true) is { } port && port != settings.Port)
            {
                ServerSettingsStore.Save(server.InstanceDir, settings, settings with { Port = port });
                server.ServerSettings.Reload();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Moved on its first start instead.
        }
    }

    private void Reload()
    {
        // Existing view models are kept, so a running server keeps its console.
        var existing = Servers.ToDictionary(s => s.Id);
        Servers.Clear();
        foreach (var server in _store.LoadAll())
        {
            Servers.Add(existing.GetValueOrDefault(server.Id) ?? NewServer(server));
        }
    }

    private ServerViewModel NewServer(InstanceMetadata metadata)
    {
        var server = new ServerViewModel(metadata, _controller, _store, AppSettings, _backups, _dialogs, _firewall, _portForwards, _tunnels, _clipboard, _mods, _discord, _faces);
        server.Deleted += OnServerDeleted;
        server.Duplicated += OnServerDuplicated;
        server.TabRequested += OnTabRequested;
        return server;
    }

    /// <summary>Raised when a server asks for one of its tabs; the window selects the server and switches tabs.</summary>
    public event EventHandler<ServerTab>? TabRequested;

    private void OnTabRequested(object? sender, ServerTab tab)
    {
        SelectedServer = sender as ServerViewModel ?? SelectedServer;
        TabRequested?.Invoke(this, tab);
    }

    private void OnBackupCopyFailed(string instanceDir, string message)
    {
        if (Servers.FirstOrDefault(s => string.Equals(s.InstanceDir, instanceDir, StringComparison.OrdinalIgnoreCase)) is { } server)
        {
            server.Backups.ErrorText = message;
        }
    }

    private void OnServerDuplicated(object? sender, InstanceMetadata copy)
    {
        Reload();
        SelectedServer = Servers.FirstOrDefault(s => s.Id == copy.Id);
        GiveOwnPort(SelectedServer);
    }

    /// <summary>Drops a deleted server from the list and selects its neighbour, or nothing when the list is empty.</summary>
    private void OnServerDeleted(object? sender, EventArgs e)
    {
        if (sender is not ServerViewModel server)
        {
            return;
        }

        server.Deleted -= OnServerDeleted;
        server.Duplicated -= OnServerDuplicated;
        var index = Servers.IndexOf(server);
        Servers.Remove(server);
        if (SelectedServer == server || SelectedServer is null)
        {
            SelectedServer = Servers.Count == 0 ? null : Servers[Math.Clamp(index, 0, Servers.Count - 1)];
        }
    }
}
