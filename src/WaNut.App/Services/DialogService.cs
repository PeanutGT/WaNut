using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using WaNut.App.Controls;
using WaNut.App.Resources;
using WaNut.App.ViewModels;
using WaNut.App.Views;
using WaNut.Core.Backups;
using WaNut.Core.Instances;
using WaNut.Core.Remote;
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

    public WaNut.Core.Mods.ModVersion? ShowModDetails(ModDetailsViewModel details)
    {
        var window = new ModDetailsWindow(details) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? details.Chosen : null;
    }

    public void ShowAppSettings()
    {
        var window = services.GetRequiredService<AppSettingsWindow>();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    public string? ShowImportWorld(ServerViewModel server)
    {
        var window = services.GetRequiredService<ImportWorldWindow>();
        window.ViewModel.Initialize(server);
        window.Owner = Application.Current.MainWindow;
        return window.ShowDialog() == true ? window.ViewModel.Notes : null;
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
        var panel = services.GetRequiredService<DeleteServerPanel>();
        panel.ViewModel.Initialize(server);
        var deleted = InlinePanel.Show(Application.Current.MainWindow, panel);
        keptZip = deleted ? panel.ViewModel.KeptZip : null;
        return deleted;
    }

    public InstanceMetadata? ShowDuplicateServer(ServerViewModel server)
    {
        var panel = services.GetRequiredService<DuplicateServerPanel>();
        panel.ViewModel.Initialize(server);
        return InlinePanel.Show(Application.Current.MainWindow, panel) ? panel.ViewModel.Copy : null;
    }

    public string? AskResetWorld(string serverName)
    {
        var panel = new ResetWorldPanel(string.Format(Text.Get("Reset_Question"), serverName));
        return InlinePanel.Show(Application.Current.MainWindow, panel) ? panel.Seed : null;
    }

    public string? AskBan(string playerName)
    {
        var panel = new BanPanel(string.Format(Text.Get("Ban_Question"), playerName));
        return InlinePanel.Show(Application.Current.MainWindow, panel) ? panel.Reason : null;
    }

    public IReadOnlyCollection<WorldPart>? AskRestore(string serverName, string backupDate, IReadOnlyList<WorldPart> parts)
    {
        var window = new RestoreWindow(string.Format(Text.Get("Restore_Question"), serverName, backupDate), parts) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.Parts : null;
    }

    public bool Confirm(string message) => Ask(message, isQuestion: true);

    public void Inform(string message) => Ask(message, isQuestion: false);

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

    public string? PickModpack()
    {
        var dialog = new OpenFileDialog { Title = Text.Get("Modpack_PickTitle"), Filter = Text.Get("Modpack_Filter") };
        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FileName : null;
    }

    public string? PickZipToSave(string fileName)
    {
        var dialog = new SaveFileDialog { Title = Text.Get("Export_SaveZipTitle"), Filter = Text.Get("World_ZipFilter"), FileName = fileName, DefaultExt = ".zip" };
        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FileName : null;
    }

    public VpsProfile? ShowVps(VpsProfile? existing)
    {
        var window = services.GetRequiredService<VpsWindow>();
        window.ViewModel.Initialize(existing);
        window.Owner = ActiveWindow();
        return window.ShowDialog() == true ? window.ViewModel.Saved : null;
    }

    public string? PickKeyFile()
    {
        var dialog = new OpenFileDialog { Title = Text.Get("Vps_PickKeyTitle"), Filter = Text.Get("Vps_KeyFilter") };
        var sshFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        if (System.IO.Directory.Exists(sshFolder))
        {
            dialog.InitialDirectory = sshFolder;
        }

        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FileName : null;
    }

    /// <summary>A panel inside the window the user is in; a small window of its own only when none is open.</summary>
    private static bool Ask(string message, bool isQuestion)
    {
        if (ActiveWindow() is { } host && InlinePanel.CanShowIn(host))
        {
            return InlinePanel.Show(host, new ConfirmPanel(message, isQuestion));
        }

        var window = new ConfirmWindow(message, isQuestion) { WindowStartupLocation = WindowStartupLocation.CenterScreen };
        return window.ShowDialog() == true;
    }

    /// <summary>The window in front (a wizard over the main window), so a dialog opens over the one the user is in.</summary>
    private static Window? ActiveWindow() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
        ?? (Application.Current.MainWindow is { IsVisible: true } main ? main : null);
}
