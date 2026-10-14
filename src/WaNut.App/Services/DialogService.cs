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
}
