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
    private readonly IClipboardService _clipboard;
    private CancellationTokenSource? _search;
    private bool _searched;
    private bool _loading;

    public PacksViewModel(ServerViewModel server, PackLibrary library, IDialogService dialogs, IClipboardService clipboard, HotplugViewModel hotplug)
    {
        _clipboard = clipboard;
        _server = server;
        _library = library;
        _dialogs = dialogs;
        Hotplug = hotplug;
        Reload();
    }

    /// <summary>The live data pack panel, shown while the server runs on its VPS.</summary>
    public HotplugViewModel Hotplug { get; }

    [ObservableProperty]
    private ObservableCollection<InstalledPackViewModel> _datapacks = [];

    public ObservableCollection<ModResultViewModel> Results { get; } = [];

    /// <summary>The pack players get on join, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResourcePack), nameof(ResourcePackName), nameof(ShowSingleResourcePack))]
    private ServerResourcePack? _resourcePack;

    /// <summary>The resource packs WaNut manages, highest priority first. Two or more are merged into one zip.</summary>
    public ObservableCollection<ResourcePackItemViewModel> ResourcePacks { get; } = [];

    public bool HasResourcePack => ResourcePack is not null || ResourcePacks.Count > 0;

    public bool ShowResourcePackList => ResourcePacks.Count > 0;

    /// <summary>A pack set by hand or by an older WaNut, outside the list.</summary>
    public bool ShowSingleResourcePack => ResourcePack is not null && ResourcePacks.Count == 0;

    /// <summary>The pack players get is WaNut's merged zip: two or more packs, or a pack from this PC.</summary>
    public bool ShowMergeHint => ResourcePacks.Count > 1 || ResourcePacks.Any(p => PackLibrary.IsLocalPack(p.Entry));

    public string ResourcePackName => ResourcePack?.Title ?? ResourcePack?.Url ?? string.Empty;

    /// <summary>Players who say no are sent away; saved at once.</summary>
    [ObservableProperty]
    private bool _requireResourcePack;

    partial void OnRequireResourcePackChanged(bool value)
    {
        if (_loading || !HasResourcePack || PackLibrary.IsResourcePackRequired(_server.InstanceDir) == value)
        {
            return;
        }

        try
        {
            _library.SetResourcePackRequired(_server.InstanceDir, value);
            if (ResourcePack is not null)
            {
                ResourcePack = ResourcePack with { Required = value };
            }
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
    [NotifyCanExecuteChangedFor(nameof(UseCommand), nameof(RemoveCommand), nameof(ClearResourcePackCommand),
        nameof(MoveResourcePackUpCommand), nameof(MoveResourcePackDownCommand), nameof(RemoveResourcePackCommand))]
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

    /// <summary>On a running VPS server the live panel below changes packs without a stop, so the banner says what still needs one.</summary>
    public string StopToChangeText => Text.Get(Hotplug.IsAvailable ? "Packs_StopToChangeVps" : "Packs_StopToChange");

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
            var listed = _library.ListResourcePacks(_server.InstanceDir);
            ResourcePacks.Clear();
            for (var i = 0; i < listed.Count; i++)
            {
                ResourcePacks.Add(new ResourcePackItemViewModel(listed[i], i, listed.Count));
            }

            RequireResourcePack = HasResourcePack && PackLibrary.IsResourcePackRequired(_server.InstanceDir);
            var custom = _library.GetCustomPackUrl(_server.InstanceDir);
            UseOwnUrl = custom is not null || (UseOwnUrl && ShowMergeHint);
            OwnUrlText = custom?.AbsoluteUri ?? string.Empty;
            OnPropertyChanged(string.Empty);
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
        Hotplug.OnShown();
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
        Hotplug.OnServerStatusChanged();
        UseCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        ClearResourcePackCommand.NotifyCanExecuteChanged();
        MoveResourcePackUpCommand.NotifyCanExecuteChanged();
        MoveResourcePackDownCommand.NotifyCanExecuteChanged();
        RemoveResourcePackCommand.NotifyCanExecuteChanged();
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
                ? ResourcePack?.Title == result.Title || ResourcePacks.Any(p => p.Entry.ProjectId == result.Hit.ProjectId)
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
                    var change = await _library.AddResourcePackAsync(_server.InstanceDir, _server.Metadata.MinecraftVersion, item.Hit.ProjectId, item.Title, progress);
                    return DescribeChange(string.Format(Text.Get("Packs_ResourcePackSet"), item.Title), change);
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

    /// <summary>Resource pack zips from this PC: picked in a dialog (no paths) or dropped on the tab.</summary>
    [RelayCommand(CanExecute = nameof(CanChange))]
    private Task AddLocalResourcePacksAsync() => AddLocalResourcePacksAsync(_dialogs.PickResourcePackZips());

    public bool CanDrop => CanChange;

    public async Task AddLocalResourcePacksAsync(IReadOnlyList<string> paths)
    {
        var zips = paths.Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        if (zips.Count == 0 || !CanChange)
        {
            return;
        }

        await RunAsync(Text.Get("Packs_Setting"), async _ =>
        {
            var done = new List<string>();
            ResourcePackChange? last = null;
            try
            {
                foreach (var zip in zips)
                {
                    var change = await _library.AddLocalResourcePackAsync(_server.InstanceDir, _server.Metadata.MinecraftVersion, zip);
                    if (change.Changed)
                    {
                        last = change;
                        done.Add(Path.GetFileNameWithoutExtension(zip));
                    }
                }
            }
            catch (NoCompatibleVersionException e)
            {
                throw new PacksProblem(string.Format(Text.Get("Packs_NoVersion"), e.Message, _server.Metadata.MinecraftVersion));
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                throw new PacksProblem(string.Format(Text.Get("Packs_LocalAddFailed"), e.Message));
            }

            return last is null
                ? Text.Get("Packs_LocalAlreadyAdded")
                : DescribeChange(string.Format(Text.Get("Packs_ResourcePackSet"), string.Join(", ", done)), last);
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

    /// <summary>The message after a resource pack list change: the list's new state plus a warning when packs overlap.</summary>
    private string DescribeChange(string message, ResourcePackChange change)
    {
        var count = _library.ListResourcePacks(_server.InstanceDir).Count;
        if (count > 1)
        {
            message = string.Format(Text.Get("Packs_ResourcePackMerged"), message, count);
        }

        return change.Overlaps.Count == 0
            ? message
            : $"{message} {string.Format(Text.Get("Packs_ResourcePackOverlap"), change.Overlaps.Count, change.Overlaps[0].Path, change.Overlaps[0].Winner)}";
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private Task MoveResourcePackUpAsync(ResourcePackItemViewModel? item) => MoveResourcePackAsync(item, -1);

    [RelayCommand(CanExecute = nameof(CanChange))]
    private Task MoveResourcePackDownAsync(ResourcePackItemViewModel? item) => MoveResourcePackAsync(item, 1);

    private async Task MoveResourcePackAsync(ResourcePackItemViewModel? item, int step)
    {
        if (item is null)
        {
            return;
        }

        await RunAsync(Text.Get("Packs_Setting"), _ =>
        {
            try
            {
                var change = _library.MoveResourcePack(_server.InstanceDir, item.Entry.FileName, item.Index + step);
                return Task.FromResult(DescribeChange(string.Format(Text.Get("Packs_ResourcePackMoved"), item.Name), change));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new PacksProblem(string.Format(Text.Get("Packs_SaveFailed"), e.Message));
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task RemoveResourcePackAsync(ResourcePackItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await RunAsync(Text.Get("Packs_Setting"), _ =>
        {
            try
            {
                var change = _library.RemoveResourcePack(_server.InstanceDir, item.Entry.FileName);
                return Task.FromResult(DescribeChange(string.Format(Text.Get("Packs_ResourcePackCleared"), item.Name), change));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new PacksProblem(string.Format(Text.Get("Packs_SaveFailed"), e.Message));
            }
        });
    }

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

    /// <summary>The merged pack can be hosted at the user's own public link instead of being shared from this PC or the VPS.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOwnUrl))]
    private bool _useOwnUrl;

    [ObservableProperty]
    private string _ownUrlText = string.Empty;

    [ObservableProperty]
    private string? _ownUrlStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckOwnUrlCommand))]
    private bool _isCheckingUrl;

    public bool ShowOwnUrl => ShowMergeHint && UseOwnUrl;

    partial void OnUseOwnUrlChanged(bool value)
    {
        if (_loading || value)
        {
            return;
        }

        try
        {
            _library.SetCustomPackUrl(_server.InstanceDir, null);
            OwnUrlText = string.Empty;
            OwnUrlStatus = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorText = string.Format(Text.Get("Packs_SaveFailed"), e.Message);
        }
    }

    [RelayCommand]
    private void SaveOwnUrl()
    {
        ErrorText = null;
        if (ResourcePackLink.Parse(OwnUrlText) is not { } url)
        {
            OwnUrlStatus = Text.Get("Packs_OwnUrlInvalid");
            return;
        }

        try
        {
            _library.SetCustomPackUrl(_server.InstanceDir, url);
            // Shows what was saved, so a Dropbox link visibly becomes dl=1.
            OwnUrlText = url.AbsoluteUri;
            OwnUrlStatus = Text.Get("Packs_OwnUrlSaved");
            CheckOwnUrlCommand.NotifyCanExecuteChanged();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorText = string.Format(Text.Get("Packs_SaveFailed"), e.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckOwnUrl))]
    private async Task CheckOwnUrlAsync()
    {
        IsCheckingUrl = true;
        OwnUrlStatus = Text.Get("Packs_OwnUrlChecking");
        try
        {
            var check = await _library.CheckCustomPackUrlAsync(_server.InstanceDir);
            OwnUrlStatus = check.Status switch
            {
                ResourcePackLinkStatus.Match => Text.Get("Server_PackLinkOk"),
                ResourcePackLinkStatus.Different => Text.Get("Server_PackLinkDifferent"),
                ResourcePackLinkStatus.WebPage => Text.Get("Server_PackLinkWebPage"),
                _ => string.Format(Text.Get("Server_PackLinkUnreachable"), check.Detail),
            };
        }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            OwnUrlStatus = e.Message;
        }
        finally
        {
            IsCheckingUrl = false;
        }
    }

    private bool CanCheckOwnUrl() => !IsCheckingUrl && _library.GetCustomPackUrl(_server.InstanceDir) is not null;

    [RelayCommand]
    private void ShowMergedPack()
    {
        try
        {
            var path = PackLibrary.MergedPackPath(_server.InstanceDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ErrorText = string.Format(Text.Get("Mods_OpenFailed"), e.Message);
        }
    }

    [RelayCommand]
    private void CopyMergedPackPath()
    {
        if (_clipboard.TrySetText(PackLibrary.MergedPackPath(_server.InstanceDir)))
        {
            OwnUrlStatus = Text.Get("Packs_OwnUrlPathCopied");
        }
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

/// <summary>One resource pack in the priority list; <paramref name="index"/> 0 is the top.</summary>
public sealed class ResourcePackItemViewModel(ResourcePackEntry entry, int index, int count)
{
    public ResourcePackEntry Entry { get; } = entry;

    public int Index { get; } = index;

    public string Name => PackLibrary.IsLocalPack(Entry) ? $"{Entry.Title} ({Text.Get("Packs_LocalTag")})" : Entry.Title;

    public string PositionText => (Index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);

    public bool CanMoveUp => Index > 0;

    public bool CanMoveDown => Index < count - 1;

    public string AccessibleName => string.Format(Text.Get("Packs_ResourcePackRow"), PositionText, Name);

    public string RemoveLabel => string.Format(Text.Get("Mods_RemoveLabel"), Name);

    public override string ToString() => AccessibleName;
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
