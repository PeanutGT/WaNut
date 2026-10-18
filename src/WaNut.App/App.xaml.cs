using System.Net.Http;
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
using WaNut.Core.Modpacks;
using WaNut.Core.Mods;
using WaNut.Core.Network;
using WaNut.Core.Notifications;
using WaNut.Core.Players;
using WaNut.Core.Settings;
using WaNut.Core.Tunnels;
using WaNut.Core.Updates;
using WaNut.Core.Versions;
using WaNut.Core.Worlds;

namespace WaNut.App;

public partial class App : Application
{
    private readonly ThemeService _theme;
    private ServiceProvider? _services;

    public App()
    {
        _theme = new ThemeService(this);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = LauncherPaths.CreateDefault();
        var store = new AppSettingsStore(paths);
        WaNut.App.Resources.UiLanguage.Apply(store.Load().Language);
        _theme.Start(store.Load().Theme);
        if (!store.Load().SetupDone)
        {
            // The setup window opens before the main window, so closing it must not end the app.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            new SetupWindow(new SetupViewModel(paths, store)).ShowDialog();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        _services = ConfigureServices(paths.WithInstancesDir(store.Load().InstancesDir), _theme);
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

    private static ServiceProvider ConfigureServices(LauncherPaths paths, ThemeService theme)
    {
        var services = new ServiceCollection();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        services.AddSingleton(paths);
        services.AddSingleton(theme);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => WaNutHttp.CreateClient(version));
        services.AddSingleton<FileDownloader>();
        services.AddSingleton<MojangVersionService>();
        services.AddSingleton<PaperClient>();
        services.AddSingleton<FabricClient>();
        services.AddSingleton<ForgeClient>();
        services.AddSingleton<NeoForgeClient>();
        services.AddSingleton<ForgeInstallerRunner>();
        services.AddSingleton<ServerVersionCatalog>();
        services.AddSingleton<AdoptiumClient>();
        services.AddSingleton<JavaRuntimeManager>();
        services.AddSingleton<InstanceStore>();
        services.AddSingleton<ServerCreator>();
        services.AddSingleton<ServerLaunchPlanner>();
        services.AddSingleton<ServerHost>();
        services.AddSingleton<AppSettingsStore>();
        services.AddSingleton<BackupStore>();
        services.AddSingleton<MojangProfiles>();
        services.AddSingleton<WorldImporter>();
        services.AddSingleton<WorldExporter>();
        services.AddSingleton<InstanceDeleter>();
        services.AddSingleton<InstanceCopier>();
        services.AddSingleton<IFirewall, WindowsFirewall>();
        services.AddSingleton<FirewallService>();
        services.AddSingleton<IPortForwarder, RouterPortForwarder>();
        services.AddSingleton<PortForwardService>();
        services.AddSingleton(s => new PlayitApi(s.GetRequiredService<HttpClient>()));
        services.AddSingleton<PlayitFiles>();
        services.AddSingleton<PlayitAgentDownloader>();
        services.AddSingleton<PlayitTunnels>();
        services.AddSingleton<TunnelService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<AppUpdater>();
        services.AddSingleton<ModrinthClient>();
        services.AddSingleton<ModLibrary>();
        services.AddSingleton<ModpackInstaller>();
        services.AddSingleton<DiscordWebhook>();
        services.AddSingleton<DiscordNotifier>();
        services.AddSingleton<BackupCopyService>();
        services.AddSingleton<TrayIcon>();

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ServerController>();
        services.AddSingleton<AppSettingsViewModel>();
        services.AddSingleton<UpdateViewModel>();
        services.AddSingleton<GuideViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<CreateServerViewModel>();
        services.AddTransient<CreateServerWindow>();
        services.AddTransient<AppSettingsWindow>();
        services.AddTransient<ServersFolderViewModel>();
        services.AddTransient<ChangeServerViewModel>();
        services.AddTransient<ChangeServerWindow>();
        services.AddTransient<ImportWorldViewModel>();
        services.AddTransient<ImportWorldWindow>();
        services.AddTransient<ExportWorldViewModel>();
        services.AddTransient<ExportWorldWindow>();
        services.AddTransient<DeleteServerViewModel>();
        services.AddTransient<DeleteServerWindow>();
        services.AddTransient<DuplicateServerViewModel>();
        services.AddTransient<DuplicateServerWindow>();

        return services.BuildServiceProvider();
    }
}
