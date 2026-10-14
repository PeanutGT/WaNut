using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Services;
using WaNut.Core.Instances;

namespace WaNut.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly InstanceStore _store;
    private readonly IDialogService _dialogs;

    public MainViewModel(InstanceStore store, IDialogService dialogs)
    {
        _store = store;
        _dialogs = dialogs;
        Reload();
    }

    public ObservableCollection<InstanceMetadata> Servers { get; } = [];

    [ObservableProperty]
    private InstanceMetadata? _selectedServer;

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
        Servers.Clear();
        foreach (var server in _store.LoadAll())
        {
            Servers.Add(server);
        }
    }
}
