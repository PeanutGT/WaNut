using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Settings;
using WaNut.Core.Worlds;

namespace WaNut.App.ViewModels;

/// <summary>
/// Picks the world to import: from the single-player worlds of every launcher WaNut knows, or from a folder or
/// .zip the user chooses. A world played with mods needs an extra tick, since its mod blocks will disappear.
/// </summary>
public sealed partial class WorldPickerViewModel : ObservableObject
{
    // The oldest version WaNut offers (blueprint decision 11); older worlds can only be upgraded.
    private static readonly Version OldestServerVersion = new(1, 16, 5);

    private readonly IDialogService _dialogs;
    private bool _scanned;

    public WorldPickerViewModel(IDialogService dialogs) => _dialogs = dialogs;

    /// <summary>Raised when another world is picked.</summary>
    public event Action? SelectionChanged;

    public ObservableCollection<WorldOptionViewModel> Worlds { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Source), nameof(ShowModdedWarning), nameof(ModdedWarningText), nameof(IsAccepted), nameof(ShowOldWorldHint))]
    private WorldOptionViewModel? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAccepted))]
    private bool _moddedAccepted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoWorlds))]
    private bool _isScanning;

    [ObservableProperty]
    private string? _errorText;

    public WorldSource? Source => Selected?.Source;

    public bool ShowNoWorlds => !IsScanning && Worlds.Count == 0;

    public bool ShowModdedWarning => Source?.Details.IsModded == true;

    public string ModdedWarningText
    {
        get
        {
            if (Source is null)
            {
                return string.Empty;
            }

            var loaders = string.Join(", ", Source.Details.ModLoaders.Select(l => l is "modded" ? Text.Get("World_UnknownLoader") : char.ToUpperInvariant(l[0]) + l[1..]));
            var mods = Source.ModFiles.Count > 0 ? string.Format(Text.Get("World_ModCount"), Source.ModFiles.Count) : string.Empty;
            return string.Format(Text.Get("World_ModdedWarning"), loaders, mods);
        }
    }

    /// <summary>True once a picked modded world has its extra tick.</summary>
    public bool IsAccepted => Source is not null && (!ShowModdedWarning || ModdedAccepted);

    /// <summary>Worlds before 1.16.5 (or before 1.9, which kept no version) can only be opened by upgrading them.</summary>
    public bool ShowOldWorldHint => Source is not null
        && (Source.Details.VersionName is not { } version || GameFeatures.ParseVersion(version) is var v && v.Major == 1 && v < OldestServerVersion);

    /// <summary>True when <paramref name="serverVersion"/> is older than the version that last saved the picked world.</summary>
    public bool IsNewerThan(string? serverVersion) =>
        Source?.Details.VersionName is { } world && serverVersion is not null
        && GameFeatures.ParseVersion(world) > GameFeatures.ParseVersion(serverVersion);

    /// <summary>Lists the launchers' worlds the first time it is called.</summary>
    public async Task ScanOnceAsync()
    {
        if (_scanned)
        {
            return;
        }

        _scanned = true;
        IsScanning = true;
        try
        {
            var found = await Task.Run(() => WorldScanner.Scan(SaveLocations.FindDefault()));
            foreach (var world in found)
            {
                Worlds.Add(new WorldOptionViewModel(world));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorText = string.Format(Text.Get("World_ScanFailed"), e.Message);
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void ChooseFolder()
    {
        if (_dialogs.PickFolder() is { } dir)
        {
            Pick(() => WorldScanner.FromFolder(dir));
        }
    }

    [RelayCommand]
    private void ChooseZip()
    {
        if (_dialogs.PickZipToOpen() is { } zip)
        {
            Pick(() => WorldScanner.FromZip(zip));
        }
    }

    partial void OnSelectedChanged(WorldOptionViewModel? value)
    {
        ModdedAccepted = false;
        SelectionChanged?.Invoke();
    }

    private void Pick(Func<WorldSource> read)
    {
        ErrorText = null;
        try
        {
            var option = new WorldOptionViewModel(read());
            Worlds.Insert(0, option);
            OnPropertyChanged(nameof(ShowNoWorlds));
            Selected = option;
        }
        catch (WorldException e)
        {
            ErrorText = Text.Get($"World_Problem_{e.Problem}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorText = string.Format(Text.Get("World_ReadFailed"), e.Message);
        }
    }
}
