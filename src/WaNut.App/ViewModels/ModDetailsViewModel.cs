using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.Core.Mods;

namespace WaNut.App.ViewModels;

/// <summary>
/// The details window of one Modrinth project: its description, pictures and every version that runs on this server,
/// each with its own Install button. Picking a version closes the window; the Mods tab does the install.
/// </summary>
public sealed partial class ModDetailsViewModel : ObservableObject
{
    private readonly ModrinthClient _modrinth;
    private readonly ModPlatform _platform;
    private readonly string _gameVersion;

    public ModDetailsViewModel(ModrinthClient modrinth, ModResultViewModel result, ModPlatform platform, string gameVersion, bool canInstall)
    {
        _modrinth = modrinth;
        _platform = platform;
        _gameVersion = gameVersion;
        Result = result;
        CanInstall = canInstall && !result.IsInstalled;
    }

    public ModResultViewModel Result { get; }

    public string Title => Result.Title;

    public ImageSource? Icon => Result.Icon;

    public bool CanInstall { get; }

    /// <summary>Why the Install buttons are missing, or null when they show.</summary>
    public string? InstallNote => Result.IsInstalled ? Text.Get("ModDetails_Installed")
        : !CanInstall ? Text.Get("ModDetails_StopToInstall")
        : null;

    public ObservableCollection<ModVersionItem> Versions { get; } = [];

    public ObservableCollection<ImageSource> Gallery { get; } = [];

    [ObservableProperty]
    private string _about = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoVersions))]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _errorText;

    public bool ShowNoVersions => !IsLoading && ErrorText is null && Versions.Count == 0;

    /// <summary>The version the user picked, once the window closes with one.</summary>
    public ModVersion? Chosen { get; private set; }

    public event Action<bool>? CloseRequested;

    public async Task LoadAsync()
    {
        try
        {
            var details = _modrinth.GetProjectDetailsAsync(Result.Hit.ProjectId);
            var versions = _modrinth.GetVersionsAsync(Result.Hit.ProjectId, _platform, _gameVersion);
            var project = await details;
            About = MarkdownText.ToPlain(project.Body.Length > 0 ? project.Body : project.Description);
            foreach (var picture in project.Gallery.Take(8))
            {
                if (ModResultViewModel.LoadImage(picture, 480) is { } image)
                {
                    Gallery.Add(image);
                }
            }

            foreach (var version in await versions)
            {
                Versions.Add(new ModVersionItem(version));
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            ErrorText = string.Format(Text.Get("ModDetails_LoadFailed"), e.Message);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowNoVersions));
        }
    }

    [RelayCommand]
    private void Install(ModVersionItem? item)
    {
        if (item is null || !CanInstall)
        {
            return;
        }

        Chosen = item.Version;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void OpenPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Result.Hit.PageUrl.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ErrorText = string.Format(Text.Get("Mods_OpenFailed"), e.Message);
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(false);
}

/// <summary>One version row in the details window.</summary>
public sealed class ModVersionItem(ModVersion version)
{
    public ModVersion Version { get; } = version;

    public string Number => Version.VersionNumber;

    public string TypeText => Version.VersionType switch
    {
        "beta" => Text.Get("ModDetails_Beta"),
        "alpha" => Text.Get("ModDetails_Alpha"),
        _ => Text.Get("ModDetails_Release"),
    };

    public bool IsRelease => Version.VersionType == "release";

    public string DateText => Version.Published?.ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;

    public string InstallLabel => string.Format(Text.Get("ModDetails_InstallLabel"), Number);

    public string AccessibleName => $"{Number}, {TypeText}, {DateText}";

    public override string ToString() => AccessibleName;
}
