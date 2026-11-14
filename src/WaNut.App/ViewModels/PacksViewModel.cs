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
using WaNut.Core.Packs;

namespace WaNut.App.ViewModels;

/// <summary>
/// The Packs tab, for every server type: data packs in the world's datapacks folder, and the resource pack
/// server.properties offers players on join, both found on Modrinth. Changes wait until the server is stopped.
/// </summary>
public sealed partial class PacksViewModel : ObservableObject
{
    private readonly ServerViewModel _server;
    private readonly PackLibrary _library;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _search;
    private bool _searched;
    private bool _loading;

    public PacksViewModel(ServerViewModel server, PackLibrary library, IDialogService dialogs)
    {
        _server = server;
        _library = library;
        _dialogs = dialogs;
        Reload();
    }

    [ObservableProperty]
    private ObservableCollection<InstalledPackViewModel> _datapacks = [];

    public ObservableCollection<ModResultViewModel> Results { get; } = [];

    /// <summary>The pack players get on join, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResourcePack), nameof(ResourcePackName))]
    private ServerResourcePack? _resourcePack;

    public bool HasResourcePack => ResourcePack is not null;

    public string ResourcePackName => ResourcePack?.Title ?? ResourcePack?.Url ?? string.Empty;

    /// <summary>Players who say no are sent away; saved at once.</summary>
    [ObservableProperty]
    private bool _requireResourcePack;

    partial void OnRequireResourcePackChanged(bool value)
    {
        if (_loading || ResourcePack is null || ResourcePack.Required == value)
        {
            return;
        }

        try
        {
            _library.SetResourcePackRequired(_server.InstanceDir, value);
            ResourcePack = ResourcePack with { Required = value };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorText = string.Format(Text.Get("Packs_SaveFailed"), e.Message);
        }
    }

    /// <summary>False lists data packs, true resource packs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDataPacks), nameof(FindHeader), nameof(UseText), nameof(InUseText))]
    private bool _isResourcePacks;

    partial void OnIsResourcePacksChanged(bool value) => _ = SearchAsync();

    public bool IsDataPacks
    {
        get => !IsResourcePacks;
        set => IsResourcePacks = !value;
    }

    public string FindHeader => Text.Get(IsResourcePacks ? "Packs_FindResourcePacks" : "Packs_FindDataPacks");

    public string FilterText => string.Format(Text.Get("Packs_Filter"), _server.Metadata.MinecraftVersion);

    public IReadOnlyList<KeyValuePair<ModSort, string>> SortOptions { get; } =
    [
        new(ModSort.Relevance, Text.Get("Mods_SortRelevance")),
        new(ModSort.Downloads, Text.Get("Mods_SortDownloads")),
        new(ModSort.Newest, Text.Get("Mods_SortNewest")),
        new(ModSort.Updated, Text.Get("Mods_SortUpdated")),
    ];

    [ObservableProperty]
    private ModSort _sort = ModSort.Relevance;

    partial void OnSortChanged(ModSort value)
    {
        if (_searched)
        {
            _ = SearchAsync();
        }
    }

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
    [NotifyCanExecuteChangedFor(nameof(UseCommand), nameof(RemoveCommand), nameof(ClearResourcePackCommand))]
    private string? _workText;

    [ObservableProperty]
    private double _workFraction;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private string? _doneText;

    public bool IsWorking => WorkText is not null;

    public bool CanChange => _server.Status == ServerStatus.Stopped && !_server.IsPreparing && !IsWorking;

    public bool ShowStopToChange => _server.IsRunning;

    public bool CanLoadMore => Results.Count < TotalHits;

    public bool ShowNoResults => _searched && !IsSearching && Results.Count == 0;

    public string DatapacksHeader => string.Format(Text.Get("Packs_InstalledDataPacks"), Datapacks.Count);

    public void Reload()
    {
        var packs = new List<InstalledPackViewModel>();
        try
        {
            packs.AddRange(_library.ListDatapacks(_server.InstanceDir).Select(p => new InstalledPackViewModel(p)));
            _loading = true;
            ResourcePack = _library.GetResourcePack(_server.InstanceDir);
            RequireResourcePack = ResourcePack?.Required ?? false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ErrorText = string.Format(Text.Get("Packs_ListFailed"), e.Message);
        }
        finally
        {
            _loading = false;
        }

        Datapacks = new ObservableCollection<InstalledPackViewModel>(packs);
        MarkInstalled();
        OnPropertyChanged(nameof(DatapacksHeader));
    }

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

    public void OnShown()
    {
        if (IsWorking)
        {
            return;
        }

        Reload();
        if (!_searched && !IsSearching)
        {
            _ = SearchAsync();
        }
    }

    internal void OnServerStatusChanged()
    {
        OnPropertyChanged(nameof(CanChange));
        OnPropertyChanged(nameof(ShowStopToChange));
        UseCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        ClearResourcePackCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private Task SearchAsync() => LoadAsync(append: false);

    [RelayCommand(CanExecute = nameof(CanLoadMoreNow))]
    private Task LoadMoreAsync() => LoadAsync(append: true);

    private bool CanLoadMoreNow() => CanLoadMore && !IsSearching;

    private PackKind Kind => IsResourcePacks ? PackKind.ResourcePack : PackKind.DataPack;

    private async Task LoadAsync(bool append)
    {
        _search?.Cancel();
        var search = _search = new CancellationTokenSource();
        ErrorText = null;
        IsSearching = true;
        try
        {
            var sort = Sort == ModSort.Relevance && Query.Trim().Length == 0 ? (ModSort?)null : Sort;
            var page = await _library.Modrinth.SearchPacksAsync(Kind, _server.Metadata.MinecraftVersion, Query, append ? Results.Count : 0, sort, search.Token);
            if (!append)
            {
                Results.Clear();
            }

            foreach (var hit in page.Hits.Where(h => Results.All(r => r.Hit.ProjectId != h.ProjectId)))
            {
                Results.Add(new ModResultViewModel(hit, false, showServerSide: false));
            }

            MarkInstalled();
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

    /// <summary>Results already on the server show "Installed" (data packs) or "In use" (the resource pack).</summary>
    private void MarkInstalled()
    {
        var installed = Datapacks.Select(p => p.Pack.ProjectId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var result in Results)
        {
            result.IsInstalled = IsResourcePacks
                ? ResourcePack?.Title == result.Title
                : installed.Contains(result.Hit.ProjectId);
        }

        UseCommand.NotifyCanExecuteChanged();
    }

    public string UseText => Text.Get(IsResourcePacks ? "Packs_UseResourcePack" : "Mods_Install");

    public string InUseText => Text.Get(IsResourcePacks ? "Packs_InUse" : "Mods_Installed");

    partial void OnIsResourcePacksChanging(bool value)
    {
        Results.Clear();
        TotalHits = 0;
    }

    /// <summary>Installs a data pack, or makes a resource pack the one players get.</summary>
    [RelayCommand(CanExecute = nameof(CanUse))]
    private async Task UseAsync(ModResultViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var resource = IsResourcePacks;
        await RunAsync(string.Format(Text.Get(resource ? "Packs_Setting" : "Mods_Installing"), item.Title), async progress =>
        {
            try
            {
                if (resource)
                {
                    await _library.SetResourcePackAsync(_server.InstanceDir, _server.Metadata.MinecraftVersion, item.Hit.ProjectId, item.Title, RequireResourcePack);
                    return string.Format(Text.Get("Packs_ResourcePackSet"), item.Title);
                }

                return await _library.InstallDatapackAsync(_server.InstanceDir, _server.Metadata.MinecraftVersion, item.Hit.ProjectId, item.Title, progress)
                    ? string.Format(Text.Get("Packs_DataPackInstalled"), item.Title)
                    : string.Format(Text.Get("Mods_AlreadyInstalled"), item.Title);
            }
            catch (NoCompatibleVersionException)
            {
                throw new PacksProblem(string.Format(Text.Get("Packs_NoVersion"), item.Title, _server.Metadata.MinecraftVersion));
            }
            catch (DownloadVerificationException)
            {
                throw new PacksProblem(string.Format(Text.Get("Mods_VerifyFailed"), item.Title));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                throw new PacksProblem(string.Format(Text.Get("Mods_InstallFailed"), item.Title, e.Message));
            }
        });
    }

    private bool CanUse(ModResultViewModel? item) => item is { IsInstalled: false } && CanChange;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync(InstalledPackViewModel? item)
    {
        if (item is null || !_dialogs.Confirm(string.Format(Text.Get("Packs_RemoveConfirm"), item.Name, _server.Name)))
        {
            return;
        }

        await RunAsync(Text.Get("Mods_Removing"), _ =>
        {
            try
            {
                _library.RemoveDatapack(_server.InstanceDir, item.Pack);
                return Task.FromResult(string.Format(Text.Get("Mods_Removed"), item.Name));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new PacksProblem(string.Format(Text.Get("Mods_RemoveFailed"), item.Name, e.Message));
            }
        });
    }

    private bool CanRemove(InstalledPackViewModel? item) => item is not null && CanChange;

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task ClearResourcePackAsync()
    {
        var name = ResourcePackName;
        await RunAsync(Text.Get("Packs_Setting"), _ =>
        {
            try
            {
                _library.ClearResourcePack(_server.InstanceDir);
                return Task.FromResult(string.Format(Text.Get("Packs_ResourcePackCleared"), name));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new PacksProblem(string.Format(Text.Get("Packs_SaveFailed"), e.Message));
            }
        });
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            var dir = PackLibrary.DatapacksDir(_server.InstanceDir);
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
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
        catch (PacksProblem e)
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
    private sealed class PacksProblem(string message) : Exception(message);
}

/// <summary>One zip or folder in the world's datapacks folder.</summary>
public sealed class InstalledPackViewModel(InstalledPack pack)
{
    public InstalledPack Pack { get; } = pack;

    public string Name => Pack.DisplayName;

    public string DetailText => Pack.Title is null ? Text.Get("Mods_ByHand") : Pack.FileName;

    public string AccessibleName => $"{Name}, {DetailText}";

    public string RemoveLabel => string.Format(Text.Get("Mods_RemoveLabel"), Name);

    public override string ToString() => AccessibleName;
}
