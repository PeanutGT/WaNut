using System.Reflection;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WaNut.App.Services;
using WaNut.App.ViewModels;
using WaNut.App.Views;
using WaNut.Core;
using WaNut.Core.Backups;
using WaNut.Core.Downloads;
using WaNut.Core.Hosting;
using WaNut.Core.Http;
using WaNut.Core.Instances;
using WaNut.Core.Java;
using WaNut.Core.Network;
using WaNut.Core.Settings;
using WaNut.Core.Updates;
using WaNut.Core.Versions;

namespace WaNut.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new ThemeService(this).Start();

        var paths = LauncherPaths.CreateDefault();
        var store = new AppSettingsStore(paths);
        if (!store.Load().SetupDone)
        {
            // The setup window opens before the main window, so closing it must not end the app.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            new SetupWindow(new SetupViewModel(paths, store)).ShowDialog();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        _services = ConfigureServices(paths.WithInstancesDir(store.Load().InstancesDir));
        _services.GetRequiredService<LauncherPaths>().EnsureCreated();

        var mainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        if (_services.GetRequiredService<AppSettingsViewModel>().CheckForUpdates)
        {
            _ = _services.GetRequiredService<UpdateViewModel>().CheckOnStartupAsync();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices(LauncherPaths paths)
    {
        var services = new ServiceCollection();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => WaNutHttp.CreateClient(version));
        services.AddSingleton<FileDownloader>();
        services.AddSingleton<MojangVersionService>();
        services.AddSingleton<PaperClient>();
        services.AddSingleton<FabricClient>();
        services.AddSingleton<ServerVersionCatalog>();
        services.AddSingleton<AdoptiumClient>();
        services.AddSingleton<JavaRuntimeManager>();
        services.AddSingleton<InstanceStore>();
        services.AddSingleton<ServerCreator>();
        services.AddSingleton<ServerLaunchPlanner>();
        services.AddSingleton<ServerHost>();
        services.AddSingleton<AppSettingsStore>();
        services.AddSingleton<BackupStore>();
        services.AddSingleton<IFirewall, WindowsFirewall>();
        services.AddSingleton<FirewallService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<AppUpdater>();

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ServerController>();
        services.AddSingleton<AppSettingsViewModel>();
        services.AddSingleton<UpdateViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<CreateServerViewModel>();
        services.AddTransient<CreateServerWindow>();
        services.AddTransient<AppSettingsWindow>();
        services.AddTransient<ServersFolderViewModel>();
        services.AddTransient<ChangeServerViewModel>();
        services.AddTransient<ChangeServerWindow>();

        return services.BuildServiceProvider();
    }
}
