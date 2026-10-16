using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using WaNut.App.Resources;
using WaNut.App.ViewModels;
using WaNut.App.Views;
using WaNut.Core.Backups;
using WaNut.Core.Instances;
using WaNut.Core.Worlds;

namespace WaNut.App.Services;

public sealed class DialogService(IServiceProvider services) : IDialogService
{
    public InstanceMetadata? ShowCreateServer()
    {
        var window = services.GetRequiredService<CreateServerWindow>();
        window.Owner = Application.Current.MainWindow;
        return window.ShowDialog() == true ? window.ViewModel.Created : null;
    }

    public InstanceMetadata? ShowChangeServer(InstanceMetadata server, string instanceDir)
    {
        var window = services.GetRequiredService<ChangeServerWindow>();
        window.ViewModel.Initialize(server, instanceDir);
        window.Owner = Application.Current.MainWindow;
        return window.ShowDialog() == true ? window.ViewModel.Changed : null;
    }

    public void ShowAppSettings()
    {
        var window = services.GetRequiredService<AppSettingsWindow>();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    public WorldImportResult? ShowImportWorld(ServerViewModel server)
    {
        var window = services.GetRequiredService<ImportWorldWindow>();
        window.ViewModel.Initialize(server);
        window.Owner = Application.Current.MainWindow;
        return window.ShowDialog() == true ? window.ViewModel.Result : null;
    }

    public WorldExportResult? ShowExportWorld(ServerViewModel server, BackupInfo? backup)
    {
        var window = services.GetRequiredService<ExportWorldWindow>();
        window.ViewModel.Initialize(server, backup);
        window.Owner = Application.Current.MainWindow;
        return window.ShowDialog() == true ? window.ViewModel.Result : null;
    }

    public bool ShowDeleteServer(ServerViewModel server, out string? keptZip)
    {
        var window = services.GetRequiredService<DeleteServerWindow>();
        window.ViewModel.Initialize(server);
        window.Owner = Application.Current.MainWindow;
        var deleted = window.ShowDialog() == true;
        keptZip = deleted ? window.ViewModel.KeptZip : null;
        return deleted;
    }

    public InstanceMetadata? ShowDuplicateServer(ServerViewModel server)
    {
        var window = services.GetRequiredService<DuplicateServerWindow>();
        window.ViewModel.Initialize(server);
        window.Owner = Application.Current.MainWindow;
        return window.ShowDialog() == true ? window.ViewModel.Copy : null;
    }

    public string? AskResetWorld(string serverName)
    {
        var window = new ResetWorldWindow(string.Format(Text.Get("Reset_Question"), serverName)) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.Seed : null;
    }

    public bool Confirm(string message) => Show(new ConfirmWindow(message));

    public void Inform(string message) => Show(new ConfirmWindow(message, isQuestion: false));

    public string? PickFolder(string? title = null)
    {
        var dialog = new OpenFolderDialog { Title = title ?? Text.Get("World_PickFolderTitle") };
        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FolderName : null;
    }

    public string? PickImage()
    {
        var dialog = new OpenFileDialog { Title = Text.Get("Icon_PickTitle"), Filter = Text.Get("Icon_Filter") };
        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FileName : null;
    }

    public string? PickZipToOpen()
    {
        var dialog = new OpenFileDialog { Title = Text.Get("World_PickZipTitle"), Filter = Text.Get("World_ZipFilter") };
        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FileName : null;
    }

    public string? PickZipToSave(string fileName)
    {
        var dialog = new SaveFileDialog { Title = Text.Get("Export_SaveZipTitle"), Filter = Text.Get("World_ZipFilter"), FileName = fileName, DefaultExt = ".zip" };
        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FileName : null;
    }

    private static bool Show(ConfirmWindow window)
    {
        if (ActiveWindow() is { } owner)
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return window.ShowDialog() == true;
    }

    /// <summary>The window in front (a wizard over the main window), so a dialog opens over the one the user is in.</summary>
    private static Window? ActiveWindow() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
        ?? (Application.Current.MainWindow is { IsVisible: true } main ? main : null);
}
