using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WaNut.App.ViewModels;
using WaNut.App.Views;
using WaNut.Core;

namespace WaNut.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = ConfigureServices();
        _services.GetRequiredService<LauncherPaths>().EnsureCreated();
        _services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton(LauncherPaths.CreateDefault());
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
