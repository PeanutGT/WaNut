using System.Reflection;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WaNut.App.Services;
using WaNut.App.ViewModels;
using WaNut.App.Views;
using WaNut.Core;
using WaNut.Core.Downloads;
using WaNut.Core.Hosting;
using WaNut.Core.Http;
using WaNut.Core.Instances;
using WaNut.Core.Java;
using WaNut.Core.Versions;

namespace WaNut.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = ConfigureServices();
        _services.GetRequiredService<LauncherPaths>().EnsureCreated();

        var mainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        services.AddSingleton(LauncherPaths.CreateDefault());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => WaNutHttp.CreateClient(version));
        services.AddSingleton<FileDownloader>();
        services.AddSingleton<MojangVersionService>();
        services.AddSingleton<AdoptiumClient>();
        services.AddSingleton<JavaRuntimeManager>();
        services.AddSingleton<InstanceStore>();
        services.AddSingleton<ServerCreator>();
        services.AddSingleton<ServerLaunchPlanner>();
        services.AddSingleton<ServerHost>();

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ServerController>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<CreateServerViewModel>();
        services.AddTransient<CreateServerWindow>();

        return services.BuildServiceProvider();
    }
}
