using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Hosting;
using WaNut.Core.Jvm;
using WaNut.Core.Settings;

namespace WaNut.App.ViewModels;

/// <summary>
/// The Server tab: RAM, port, account checks and, in Advanced mode, custom JVM arguments. RAM and JVM arguments
/// live in instance.json and apply at the next start; port and account checks are server.properties keys, so
/// like the Game settings they are locked while the server runs.
/// </summary>
public sealed partial class ServerSettingsViewModel : ObservableObject
{
    private readonly ServerViewModel _server;
    private readonly IDialogService _dialogs;
    private ServerSettings _loaded = new();
    private bool _loading;

    public ServerSettingsViewModel(ServerViewModel server, AppSettingsViewModel app, IDialogService dialogs)
    {
        _server = server;
        _dialogs = dialogs;
        App = app;
        var totalMb = MemoryLimits.TotalPhysicalMb();
        MaxMemoryMb = Math.Max(MemoryLimits.MaxServerMb(totalMb), server.Metadata.MemoryMb);
        Reload();
    }

    /// <summary>For the Advanced mode switch.</summary>
    public AppSettingsViewModel App { get; }

    public int MinMemoryMb => MemoryLimits.MinimumMb;

    public int MaxMemoryMb { get; }

    public int MemoryStepMb => MemoryLimits.StepMb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryText))]
    private int _memoryMb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PortHint))]
    private int _port;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOnlineModeWarning))]
    private bool _onlineMode;

    [ObservableProperty]
    private string _customJvmArguments = string.Empty;

    [ObservableProperty]
    private string? _jvmArgumentsError;

    [ObservableProperty]
    private string? _changeDoneText;

    public string MemoryText => string.Format(Text.Get("CreateServer_MemoryValue"), MemoryMb / 1024.0);

    public string PortHint => string.Format(Text.Get("Server_PortHint"), Port, ServerSettings.DefaultPort);

    public bool CanEditProperties => _server.Status == ServerStatus.Stopped;

    /// <summary>RAM and JVM arguments can change any time, but only take effect at the next start.</summary>
    public bool ShowNextStartHint => _server.IsRunning;

    public bool ShowOnlineModeWarning => !OnlineMode;

    /// <summary>Custom arguments still apply with Advanced mode off; this says so, since the box is hidden then.</summary>
    public bool ShowHiddenJvmArgumentsNote => !App.AdvancedMode && _server.Metadata.CustomJvmArguments.Trim().Length > 0;

    public void Reload()
    {
        _loading = true;
        try
        {
            MemoryMb = _server.Metadata.MemoryMb;
            CustomJvmArguments = _server.Metadata.CustomJvmArguments;
            _loaded = ServerSettingsStore.Load(_server.InstanceDir);
            Port = _loaded.Port;
            OnlineMode = _loaded.OnlineMode;
        }
        catch (IOException e)
        {
            _server.ShowError(string.Format(Text.Get("Settings_LoadFailed"), e.Message));
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Opens the "Change type or version" window. Only while stopped, since server files are replaced.</summary>
    [RelayCommand(CanExecute = nameof(CanEditProperties))]
    private void ChangeTypeOrVersion()
    {
        ChangeDoneText = null;
        if (_dialogs.ShowChangeServer(_server.Metadata, _server.InstanceDir) is { } changed)
        {
            _server.OnTypeOrVersionChanged(changed);
            ChangeDoneText = string.Format(Text.Get("Change_Done"), _server.TypeAndVersionText);
        }
    }

    /// <summary>Opens the "Delete server" window. Only while stopped and while nothing else is working on its files.</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void DeleteServer()
    {
        if (_dialogs.ShowDeleteServer(_server, out var keptZip))
        {
            _server.OnDeleted();
            if (keptZip is not null)
            {
                _dialogs.Inform(string.Format(Text.Get("Delete_DoneKept"), _server.Name, keptZip));
            }
        }
    }

    private bool CanDelete() => _server.CanDelete;

    internal void OnServerStatusChanged()
    {
        ChangeTypeOrVersionCommand.NotifyCanExecuteChanged();
        DeleteServerCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditProperties));
        OnPropertyChanged(nameof(ShowNextStartHint));
        if (_server.Status == ServerStatus.Stopped)
        {
            Reload();
        }
    }

    internal void OnAdvancedModeChanged() => OnPropertyChanged(nameof(ShowHiddenJvmArgumentsNote));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MemoryMb):
                SaveMetadata(_server.Metadata with { MemoryMb = Math.Clamp(MemoryMb, MinMemoryMb, MaxMemoryMb) });
                break;
            case nameof(CustomJvmArguments):
                SaveJvmArguments();
                break;
            case nameof(Port) or nameof(OnlineMode):
                SaveProperties();
                break;
        }
    }

    private void SaveJvmArguments()
    {
        if (JvmArguments.ParseCustom(CustomJvmArguments, out var problem) is null)
        {
            // Not saved, so the server keeps starting with the last good arguments.
            JvmArgumentsError = Text.Get($"Server_JvmProblem_{problem}");
            return;
        }

        JvmArgumentsError = null;
        SaveMetadata(_server.Metadata with { CustomJvmArguments = CustomJvmArguments.Trim() });
        OnPropertyChanged(nameof(ShowHiddenJvmArgumentsNote));
    }

    private void SaveProperties()
    {
        if (!ServerSettings.IsValidPort(Port))
        {
            _loading = true;
            Port = _loaded.Port;
            _loading = false;
            return;
        }

        var edited = new ServerSettings { Port = Port, OnlineMode = OnlineMode };
        try
        {
            ServerSettingsStore.Save(_server.InstanceDir, _loaded, edited);
            _loaded = edited;
        }
        catch (IOException e)
        {
            _server.ShowError(string.Format(Text.Get("Settings_SaveFailed"), e.Message));
        }
    }

    private void SaveMetadata(Core.Instances.InstanceMetadata metadata)
    {
        try
        {
            _server.UpdateMetadata(metadata);
        }
        catch (IOException e)
        {
            _server.ShowError(string.Format(Text.Get("Settings_SaveFailed"), e.Message));
        }
    }
}
