using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Downloads;
using WaNut.Core.Hosting;
using WaNut.Core.Mods;

namespace WaNut.App.ViewModels;

/// <summary>
/// The Mods tab: the jars in the server's mods (Fabric, Forge, NeoForge) or plugins (Paper) folder, and a Modrinth search limited to
/// the server's loader and Minecraft version. Installing and removing wait until the server is stopped, because
/// Windows locks the jars of a running server and new ones only load on start.
/// </summary>
public sealed partial class ModsViewModel : ObservableObject
{
    private readonly ServerViewModel _server;
    private readonly ModLibrary _library;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _search;
    private bool _searched;

    public ModsViewModel(ServerViewModel server, ModLibrary library, IDialogService dialogs)
    {
        _server = server;
        _library = library;
        _dialogs = dialogs;
        Reload();
    }

    public ObservableCollection<InstalledModViewModel> Installed { get; } = [];

    public ObservableCollection<ModResultViewModel> Results { get; } = [];

    /// <summary>Null for Vanilla, which loads neither mods nor plugins.</summary>
    public ModPlatform? Platform => ModPlatform.For(_server.Metadata.Type);

    public bool IsSupported => Platform is not null;

    private bool IsPlugins => Platform?.Kind == ModKind.Plugin;

    public string Header => Text.Get(IsPlugins ? "Server_TabPlugins" : "Server_TabMods");

    public string InstalledHeader => string.Format(Text.Get(IsPlugins ? "Mods_InstalledPlugins" : "Mods_InstalledMods"), Installed.Count);

    public string EmptyText => Text.Get(IsPlugins ? "Mods_EmptyPlugins" : "Mods_EmptyMods");

    public string FindHeader => Text.Get(IsPlugins ? "Mods_FindPlugins" : "Mods_FindMods");

    public string FilterText => string.Format(Text.Get(IsPlugins ? "Mods_FilterPlugins" : "Mods_FilterMods"), _server.Metadata.MinecraftVersion, Text.Get($"CreateServer_Type{_server.Metadata.Type}"));

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _isSearching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private int _totalHits;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorking), nameof(CanChange))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(RemoveCommand))]
    private string? _workText;

    [ObservableProperty]
    private double _workFraction;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private string? _doneText;

    public bool IsWorking => WorkText is not null;

    public bool CanChange => _server.Status == ServerStatus.Stopped && !_server.IsPreparing && !IsWorking;

    public bool ShowStopToChange => IsSupported && _server.IsRunning;

    public bool CanLoadMore => Results.Count < TotalHits;

    public bool ShowNoResults => _searched && !IsSearching && Results.Count == 0;

    /// <summary>Rereads the folder; after a type change the old results are for the wrong loader, so they go too.</summary>
    public void Reload()
    {
        Installed.Clear();
        if (Platform is { } platform)
        {
            try
            {
                foreach (var mod in _library.ListInstalled(_server.InstanceDir, platform))
                {
                    Installed.Add(new InstalledModViewModel(mod));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ErrorText = string.Format(Text.Get("Mods_ListFailed"), platform.FolderName, e.Message);
            }
        }

        var installedIds = InstalledProjectIds();
        foreach (var result in Results)
        {
            result.IsInstalled = installedIds.Contains(result.Hit.ProjectId);
        }

        OnPropertyChanged(nameof(InstalledHeader));
    }

    /// <summary>The server changed type or version: everything on the tab depends on it.</summary>
    internal void OnTypeOrVersionChanged()
    {
        _search?.Cancel();
        Results.Clear();
        TotalHits = 0;
        _searched = false;
        ErrorText = null;
        DoneText = null;
        Reload();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>The tab was opened: rescan the folder (files may have changed in Explorer) and show popular projects the first time.</summary>
    public void OnShown()
    {
        if (IsWorking)
        {
            return;
        }

        Reload();
        if (IsSupported && !_searched && !IsSearching)
        {
            _ = SearchAsync();
        }
    }

    internal void OnServerStatusChanged()
    {
        OnPropertyChanged(nameof(CanChange));
        OnPropertyChanged(nameof(ShowStopToChange));
        InstallCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private Task SearchAsync() => LoadAsync(append: false);

    [RelayCommand(CanExecute = nameof(CanLoadMoreNow))]
    private Task LoadMoreAsync() => LoadAsync(append: true);

    private bool CanLoadMoreNow() => CanLoadMore && !IsSearching;

    private async Task LoadAsync(bool append)
    {
        if (Platform is not { } platform)
        {
            return;
        }

        _search?.Cancel();
        var search = _search = new CancellationTokenSource();
        ErrorText = null;
        IsSearching = true;
        try
        {
            var page = await _library.Modrinth.SearchAsync(platform, _server.Metadata.MinecraftVersion, Query, append ? Results.Count : 0, search.Token);
            if (!append)
            {
                Results.Clear();
            }

            var installedIds = InstalledProjectIds();
            foreach (var hit in page.Hits.Where(h => Results.All(r => r.Hit.ProjectId != h.ProjectId)))
            {
                Results.Add(new ModResultViewModel(hit, installedIds.Contains(hit.ProjectId)));
            }

            // A page with nothing new means Modrinth has no more, whatever its total said.
            TotalHits = page.Hits.Count == 0 ? Results.Count : page.TotalHits;
            _searched = true;
        }
        catch (OperationCanceledException) when (search.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            ErrorText = string.Format(Text.Get("Mods_SearchFailed"), e.Message);
        }
        finally
        {
            if (_search == search)
            {
                IsSearching = false;
                OnPropertyChanged(nameof(ShowNoResults));
                OnPropertyChanged(nameof(CanLoadMore));
                LoadMoreCommand.NotifyCanExecuteChanged();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync(ModResultViewModel? item)
    {
        if (item is null || Platform is not { } platform)
        {
            return;
        }

        await RunAsync(string.Format(Text.Get("Mods_Installing"), item.Title), async progress =>
        {
            try
            {
                ModInstallResult result;
                try
                {
                    result = await _library.InstallAsync(
                        _server.InstanceDir, platform, _server.Metadata.MinecraftVersion, item.Hit.ProjectId, item.Title, progress: progress);
                }
                catch (IncompatibleModsException e)
                {
                    if (!_dialogs.Confirm(string.Format(Text.Get("Mods_IncompatibleConfirm"), item.Title, string.Join(", ", e.Installed))))
                    {
                        return string.Format(Text.Get("Mods_IncompatibleSkipped"), item.Title);
                    }

                    result = await _library.InstallAsync(
                        _server.InstanceDir, platform, _server.Metadata.MinecraftVersion, item.Hit.ProjectId, item.Title, allowIncompatible: true, progress);
                }

                return InstallMessage(item.Title, result);
            }
            catch (NoCompatibleVersionException)
            {
                throw new ModsProblem(string.Format(Text.Get("Mods_NoVersion"), item.Title, _server.Metadata.Type, _server.Metadata.MinecraftVersion));
            }
            catch (DownloadVerificationException)
            {
                throw new ModsProblem(string.Format(Text.Get("Mods_VerifyFailed"), item.Title));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                throw new ModsProblem(string.Format(Text.Get("Mods_InstallFailed"), item.Title, e.Message));
            }
        });
    }

    private bool CanInstall(ModResultViewModel? item) => item is { IsInstalled: false } && CanChange;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync(InstalledModViewModel? item)
    {
        if (item is null || Platform is not { } platform
            || !_dialogs.Confirm(string.Format(Text.Get("Mods_RemoveConfirm"), item.Name, _server.Name)))
        {
            return;
        }

        await RunAsync(Text.Get("Mods_Removing"), _ =>
        {
            try
            {
                _library.Remove(_server.InstanceDir, platform, item.Mod);
                return Task.FromResult(string.Format(Text.Get("Mods_Removed"), item.Name));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new ModsProblem(string.Format(Text.Get("Mods_RemoveFailed"), item.Name, e.Message));
            }
        });
    }

    private bool CanRemove(InstalledModViewModel? item) => item is not null && CanChange;

    [RelayCommand]
    private void OpenFolder()
    {
        if (Platform is not { } platform)
        {
            return;
        }

        var dir = platform.FolderIn(_server.InstanceDir);
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ErrorText = string.Format(Text.Get("Mods_OpenFailed"), e.Message);
        }
    }

    [RelayCommand]
    private void OpenPage(ModResultViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(item.Hit.PageUrl.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ErrorText = string.Format(Text.Get("Mods_OpenFailed"), e.Message);
        }
    }

    private static string InstallMessage(string title, ModInstallResult result)
    {
        if (result.Installed.Count == 0)
        {
            return string.Format(Text.Get("Mods_AlreadyInstalled"), title);
        }

        var done = result.Installed.Count == 1
            ? string.Format(Text.Get("Mods_InstallDone"), title)
            : string.Format(Text.Get("Mods_InstallDoneWithDependencies"), title, string.Join(", ", result.Installed.Skip(1)));
        return result.MissingDependencies.Count == 0
            ? done
            : done + Environment.NewLine + string.Format(Text.Get("Mods_MissingDependencies"), title, string.Join(", ", result.MissingDependencies));
    }

    private HashSet<string> InstalledProjectIds() =>
        Installed.Where(m => m.Mod.ProjectId is not null).Select(m => m.Mod.ProjectId!).ToHashSet(StringComparer.Ordinal);

    private async Task RunAsync(string workText, Func<IProgress<double>, Task<string>> work)
    {
        ErrorText = null;
        DoneText = null;
        WorkFraction = 0;
        WorkText = workText;
        _server.OnModWorkChanged();
        try
        {
            DoneText = await work(new Progress<double>(f => WorkFraction = f));
        }
        catch (ModsProblem e)
        {
            ErrorText = e.Message;
        }
        finally
        {
            WorkText = null;
            _server.OnModWorkChanged();
            Reload();
        }
    }

    /// <summary>A failure already worded for the user.</summary>
    private sealed class ModsProblem(string message) : Exception(message);
}
