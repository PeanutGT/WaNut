using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using WaNut.Core.Settings;

namespace WaNut.App.ViewModels;

/// <summary>WaNut's own settings. One instance for the whole app, so every server tab sees Advanced mode change at once.</summary>
public sealed partial class AppSettingsViewModel : ObservableObject
{
    private readonly AppSettingsStore _store;

    public AppSettingsViewModel(AppSettingsStore store)
    {
        _store = store;
        _advancedMode = store.Load().AdvancedMode;
    }

    [ObservableProperty]
    private bool _advancedMode;

    [ObservableProperty]
    private string? _errorText;

    partial void OnAdvancedModeChanged(bool value)
    {
        try
        {
            _store.Save(new AppSettings { AdvancedMode = value });
            ErrorText = null;
        }
        catch (IOException e)
        {
            ErrorText = string.Format(Resources.Text.Get("Settings_SaveFailed"), e.Message);
        }
    }
}
