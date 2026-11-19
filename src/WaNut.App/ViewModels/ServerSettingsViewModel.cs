using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
/// The Server tab: RAM, run location, icon and, in Advanced mode, custom JVM arguments. RAM and JVM arguments
/// live in instance.json and apply at the next start. The port and the account check are server.properties keys
/// and live on the Game settings tab only.
/// </summary>
public sealed partial class ServerSettingsViewModel : ObservableObject
{
    private readonly ServerViewModel _server;
    private readonly IDialogService _dialogs;
    private readonly long _thisPcMb = MemoryLimits.TotalPhysicalMb();
    private bool _loading;

    public ServerSettingsViewModel(ServerViewModel server, AppSettingsViewModel app, IDialogService dialogs)
    {
        _server = server;
        _dialogs = dialogs;
        App = app;
        CollectionChangedEventManager.AddHandler(app.VpsList, (_, _) => LoadRunLocations());
        Reload();
    }

    /// <summary>The name shown in WaNut and in friends' invite cards. The folder keeps its name; only instance.json changes.</summary>
    [ObservableProperty]
    private string _serverName = string.Empty;

    /// <summary>"This PC" and every VPS from App settings (Advanced mode only, plan decision 6A).</summary>
    public ObservableCollection<RunLocationOption> RunLocations { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunsOnVps))]
    private RunLocationOption? _runLocation;

    public bool RunsOnVps => RunLocation?.VpsId is not null;

    /// <summary>Leave the folder on the VPS when the server stops there (<see cref="Core.Instances.InstanceMetadata.KeepFilesOnVps"/>).</summary>
    [ObservableProperty]
    private bool _keepFilesOnVps;

    /// <summary>
    /// Only while stopped, and not while the newest world is still on the VPS: switching then would leave it there.
    /// </summary>
    public bool CanChangeRunLocation => _server.Status == ServerStatus.Stopped && !_server.IsPreparing && !_server.Metadata.VpsCopyIsNewer;

    public bool ShowVpsCopyNote => _server.Metadata.VpsCopyIsNewer && !_server.IsRunning;

    /// <summary>For the Advanced mode switch.</summary>
    public AppSettingsViewModel App { get; }

    public int MinMemoryMb => MemoryLimits.MinimumMb;

    /// <summary>
    /// From the RAM of where the server runs: this PC, or the VPS once it reported its RAM. Never below the saved value,
    /// so moving a server to a smaller VPS doesn't lower its memory unnoticed; <see cref="MemoryOverVpsText"/> says so.
    /// </summary>
    [ObservableProperty]
    private int _maxMemoryMb = MemoryLimits.MinimumMb;

    /// <summary>Set when the memory is more than the chosen VPS can give, so the start would be refused.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMemoryOverVps))]
    private string? _memoryOverVpsText;

    public bool ShowMemoryOverVps => MemoryOverVpsText is not null;

    public int MemoryStepMb => MemoryLimits.StepMb;

    /// <summary>Past 75% of this PC's memory on a server that runs here: allowed, with a warning.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMemoryAboveRecommended))]
    private int _recommendedMemoryMb = int.MaxValue;

    public bool ShowMemoryAboveRecommended => MemoryMb > RecommendedMemoryMb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryText))]
    [NotifyPropertyChangedFor(nameof(ShowMemoryAboveRecommended))]
    private int _memoryMb;

    [ObservableProperty]
    private string _customJvmArguments = string.Empty;

    [ObservableProperty]
    private bool _autoRestart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoStopMinutesEnabled))]
    private bool _autoStop;

    /// <summary>Minutes with nobody online before the server stops, kept when the switch is off.</summary>
    [ObservableProperty]
    private int _autoStopMinutes = DefaultAutoStopMinutes;

    public const int DefaultAutoStopMinutes = 15;

    public bool AutoStopMinutesEnabled => AutoStop;

    [ObservableProperty]
    private string? _jvmArgumentsError;

    [ObservableProperty]
    private string? _changeDoneText;

    [ObservableProperty]
    private string? _iconError;

    /// <summary>For the icon preview.</summary>
    public ServerViewModel Server => _server;

    public string MemoryText => string.Format(Text.Get("CreateServer_MemoryValue"), MemoryMb / 1024.0);

    /// <summary>server.properties and the server's own files: while stopped, and only once its newest files are here.</summary>
    public bool CanEditProperties => _server.Status == ServerStatus.Stopped && CanEditFiles;

    /// <summary>
    /// False while the newest files are on the VPS: a change here would be lost when they come back. What lives in
    /// instance.json (memory, Java arguments, auto restart and stop, keeping the files there) never syncs, so it stays
    /// editable; that is how the user can stop keeping the files on the VPS without bringing them back first.
    /// </summary>
    public bool CanEditFiles => _server.CanEditFiles;

    /// <summary>The tab was shown: with the files on the VPS this copies its settings file first.</summary>
    public void OnShown()
    {
        // The files may have changed while the tab was away (a stop, a sync from the VPS).
        if (_server.Status == ServerStatus.Stopped)
        {
            Reload();
        }

        _ = _server.EnsureVpsSettingsAsync();
    }

    /// <summary>Says why the icon, port and the rest are locked while the server is stopped with its files away.</summary>
    public bool ShowFilesOnVpsNote => !CanEditFiles && !_server.IsRunning;

    /// <summary>RAM and JVM arguments can change any time, but only take effect at the next start.</summary>
    public bool ShowNextStartHint => _server.IsRunning;

    /// <summary>Custom arguments still apply with Advanced mode off; this says so, since the box is hidden then.</summary>
    public bool ShowHiddenJvmArgumentsNote => !App.AdvancedMode && _server.Metadata.CustomJvmArguments.Trim().Length > 0;

    public void Reload()
    {
        _loading = true;
        try
        {
            ServerName = _server.Metadata.Name;
            MemoryMb = _server.Metadata.MemoryMb;
            CustomJvmArguments = _server.Metadata.CustomJvmArguments;
            AutoRestart = _server.Metadata.AutoRestart;
            KeepFilesOnVps = _server.Metadata.KeepFilesOnVps;
            AutoStop = _server.Metadata.AutoStopMinutes > 0;
            AutoStopMinutes = AutoStop ? _server.Metadata.AutoStopMinutes : DefaultAutoStopMinutes;
            LoadRunLocations();
            LighterEntities = ShowPaperTweaks && PaperTweaks.IsOn(_server.InstanceDir);
        }
        catch (IOException e)
        {
            _server.ShowError(string.Format(Text.Get("Settings_LoadFailed"), e.Message));
        }
        finally
        {
            _loading = false;
        }

        OnPropertyChanged(nameof(ShowPaperTweaks));
        OnPropertyChanged(nameof(CanEditPaperTweaks));
        OnPropertyChanged(nameof(ShowPaperTweaksStartFirst));
    }

    /// <summary>Paper 1.19+ only: the "lighter entities" switch in More settings.</summary>
    public bool ShowPaperTweaks => PaperTweaks.Supports(_server.Metadata.Type, _server.Metadata.MinecraftVersion);

    /// <summary>Paper writes the file on the first start and again when it stops, so only while stopped and after that.</summary>
    public bool CanEditPaperTweaks => CanEditProperties && PaperTweaks.HasFile(_server.InstanceDir);

    public bool ShowPaperTweaksStartFirst => ShowPaperTweaks && !PaperTweaks.HasFile(_server.InstanceDir);

    [ObservableProperty]
    private bool _lighterEntities;

    private void SavePaperTweaks()
    {
        try
        {
            PaperTweaks.Set(_server.InstanceDir, LighterEntities);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _server.ShowError(string.Format(Text.Get("Settings_SaveFailed"), e.Message));
        }
    }

    /// <summary>Lets the user pick a picture and turns it into the server's 64x64 icon.</summary>
    [RelayCommand(CanExecute = nameof(CanEditFiles))]
    private void ChooseIcon()
    {
        IconError = null;
        if (_dialogs.PickImage() is not { } picture)
        {
            return;
        }

        try
        {
            ServerIconMaker.Save(picture, _server.InstanceDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            IconError = string.Format(Text.Get("Icon_Failed"), e.Message);
        }

        _server.ReloadIcon();
        RemoveIconCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasIcon))]
    private void RemoveIcon()
    {
        IconError = null;
        try
        {
            File.Delete(Core.Instances.ServerIcon.PathIn(_server.InstanceDir));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            IconError = string.Format(Text.Get("Icon_Failed"), e.Message);
        }

        _server.ReloadIcon();
        RemoveIconCommand.NotifyCanExecuteChanged();
    }

    private bool HasIcon() => CanEditFiles && _server.HasIcon;

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

    /// <summary>Opens the "Duplicate server" window. Same rules as deleting: stopped, and nothing else using its files.</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void DuplicateServer()
    {
        if (_dialogs.ShowDuplicateServer(_server) is { } copy)
        {
            _server.OnDuplicated(copy);
        }
    }

    internal void OnServerStatusChanged()
    {
        ChangeTypeOrVersionCommand.NotifyCanExecuteChanged();
        DeleteServerCommand.NotifyCanExecuteChanged();
        DuplicateServerCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditProperties));
        OnPropertyChanged(nameof(CanEditPaperTweaks));
        OnPropertyChanged(nameof(ShowNextStartHint));
        OnPropertyChanged(nameof(ShowFilesOnVpsNote));
        OnPropertyChanged(nameof(CanChangeRunLocation));
        OnPropertyChanged(nameof(ShowVpsCopyNote));
        if (_server.Status == ServerStatus.Stopped)
        {
            Reload();
        }
    }

    /// <summary>The server's newest files left for the VPS or came back.</summary>
    internal void OnVpsStateChanged()
    {
        OnPropertyChanged(nameof(CanEditFiles));
        OnPropertyChanged(nameof(CanEditProperties));
        OnPropertyChanged(nameof(CanEditPaperTweaks));
        OnPropertyChanged(nameof(ShowFilesOnVpsNote));
        OnPropertyChanged(nameof(CanChangeRunLocation));
        OnPropertyChanged(nameof(ShowVpsCopyNote));
        ChooseIconCommand.NotifyCanExecuteChanged();
        RemoveIconCommand.NotifyCanExecuteChanged();
        ChangeTypeOrVersionCommand.NotifyCanExecuteChanged();
        DeleteServerCommand.NotifyCanExecuteChanged();
        DuplicateServerCommand.NotifyCanExecuteChanged();
    }

    /// <summary>A backup, start or mod install began or ended: deleting and duplicating wait for it.</summary>
    internal void OnFilesBusyChanged()
    {
        OnPropertyChanged(nameof(CanChangeRunLocation));
        OnPropertyChanged(nameof(ShowVpsCopyNote));
        DeleteServerCommand.NotifyCanExecuteChanged();
        DuplicateServerCommand.NotifyCanExecuteChanged();
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
            case nameof(ServerName):
                RenameServer();
                break;
            case nameof(MemoryMb):
                SaveMetadata(_server.Metadata with { MemoryMb = Math.Clamp(MemoryMb, MinMemoryMb, MaxMemoryMb) });
                UpdateMemoryLimit();
                break;
            case nameof(CustomJvmArguments):
                SaveJvmArguments();
                break;
            case nameof(AutoRestart):
                SaveMetadata(_server.Metadata with { AutoRestart = AutoRestart });
                break;
            case nameof(KeepFilesOnVps):
                SaveMetadata(_server.Metadata with { KeepFilesOnVps = KeepFilesOnVps });
                break;
            case nameof(AutoStop) or nameof(AutoStopMinutes):
                SaveAutoStop();
                break;
            case nameof(RunLocation) when RunLocation is not null && RunLocation.VpsId != _server.Metadata.VpsId:
                SaveMetadata(_server.Metadata with { VpsId = RunLocation.VpsId });
                _server.Connect.OnRunLocationChanged();
                UpdateMemoryLimit();
                break;
            case nameof(LighterEntities):
                SavePaperTweaks();
                break;
        }
    }

    private void LoadRunLocations()
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            RunLocations.Clear();
            RunLocations.Add(new RunLocationOption(null, Text.Get("Server_RunHere")));
            foreach (var vps in App.VpsList)
            {
                RunLocations.Add(new RunLocationOption(vps.Id, vps.Name));
            }

            // A VPS removed from App settings still shows, so the choice is never silently lost.
            if (_server.Metadata.VpsId is { } id && RunLocations.All(o => o.VpsId != id))
            {
                RunLocations.Add(new RunLocationOption(id, Text.Get("Server_RunVpsMissing")));
            }

            RunLocation = RunLocations.First(o => o.VpsId == _server.Metadata.VpsId);
        }
        finally
        {
            _loading = wasLoading;
        }

        UpdateMemoryLimit();
    }

    private void UpdateMemoryLimit()
    {
        var vpsMb = App.VpsList.FirstOrDefault(v => v.Id == _server.Metadata.VpsId)?.MemoryMb ?? 0;

        // A VPS not tested since this was added falls back to this PC; the start still checks the real RAM.
        var vpsLimit = MemoryLimits.MaxVpsServerMb(vpsMb);
        var limit = vpsMb > 0 ? Math.Max(MemoryLimits.MinimumMb, vpsLimit) : MemoryLimits.MaxServerMb(_thisPcMb);
        var wasLoading = _loading;
        _loading = true;
        try
        {
            MaxMemoryMb = Math.Max(limit, _server.Metadata.MemoryMb);
            RecommendedMemoryMb = vpsMb > 0 ? int.MaxValue : MemoryLimits.RecommendedServerMb(_thisPcMb);
        }
        finally
        {
            _loading = wasLoading;
        }

        MemoryOverVpsText = vpsMb <= 0 || _server.Metadata.MemoryMb <= vpsLimit ? null
            : vpsLimit < MemoryLimits.MinimumMb ? string.Format(Text.Get("Server_MemoryVpsTooSmall"), vpsMb / 1024.0)
            : string.Format(Text.Get("Server_MemoryOverVps"), vpsMb / 1024.0, limit / 1024.0);
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

    private void SaveAutoStop()
    {
        if (AutoStopMinutes is < 1 or > 1440)
        {
            _loading = true;
            AutoStopMinutes = Math.Clamp(AutoStopMinutes, 1, 1440);
            _loading = false;
        }

        SaveMetadata(_server.Metadata with { AutoStopMinutes = AutoStop ? AutoStopMinutes : 0 });
    }

    private void RenameServer()
    {
        var name = ServerName.Trim();
        if (name.Length == 0)
        {
            // An empty name is not a name: show the old one again.
            _loading = true;
            ServerName = _server.Metadata.Name;
            _loading = false;
            return;
        }

        if (name != _server.Metadata.Name)
        {
            SaveMetadata(_server.Metadata with { Name = name });
        }
    }

    private void SaveMetadata(Core.Instances.InstanceMetadata metadata)
    {
        try
        {
            _server.UpdateMetadata(metadata);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _server.ShowError(string.Format(Text.Get("Settings_SaveFailed"), e.Message));
        }
    }
}
