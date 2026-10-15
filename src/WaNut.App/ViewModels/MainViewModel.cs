using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Services;
using WaNut.Core.Backups;
using WaNut.Core.Instances;

namespace WaNut.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly InstanceStore _store;
    private readonly IDialogService _dialogs;
    private readonly ServerController _controller;
    private readonly BackupStore _backups;
    private readonly FirewallService _firewall;
    private readonly IClipboardService _clipboard;

    public MainViewModel(
        InstanceStore store,
        IDialogService dialogs,
        ServerController controller,
        AppSettingsViewModel appSettings,
        BackupStore backups,
        FirewallService firewall,
        IClipboardService clipboard,
        UpdateViewModel updates)
    {
        _firewall = firewall;
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

    [ObservableProperty]
    private ServerViewModel? _selectedServer;

    [ObservableProperty]
    private bool _isShuttingDown;

    public bool HasRunningServer => _controller.HasRunningServer;

    /// <summary>Stops the running server so its world is saved before WaNut closes.</summary>
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
    }

    private void Reload()
    {
        // Existing view models are kept, so a running server keeps its console.
        var existing = Servers.ToDictionary(s => s.Id);
        Servers.Clear();
        foreach (var server in _store.LoadAll())
        {
            Servers.Add(existing.GetValueOrDefault(server.Id) ?? new ServerViewModel(server, _controller, _store, AppSettings, _backups, _dialogs, _firewall, _clipboard));
        }
    }
}
