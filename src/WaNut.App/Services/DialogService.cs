using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WaNut.App.Views;
using WaNut.Core.Instances;

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

    public bool Confirm(string message)
    {
        var window = new ConfirmWindow(message);
        if (Application.Current.MainWindow is { IsVisible: true } owner)
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return window.ShowDialog() == true;
    }
}
