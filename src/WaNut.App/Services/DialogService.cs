using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WaNut.App.Resources;
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

    public bool Confirm(string message)
    {
        var owner = Application.Current.MainWindow;
        var result = owner is { IsVisible: true }
            ? MessageBox.Show(owner, message, Text.Get("App_Title"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(message, Text.Get("App_Title"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }
}
