using System.Net.Http;
using System.Reflection;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WaNut.App.Services;
using WaNut.App.ViewModels;
using WaNut.App.Views;
using WaNut.Core;
using WaNut.Core.Backups;
using WaNut.Core.Bridge;
using WaNut.Core.Bridge.Local;
using WaNut.Core.Bridge.Vps;
using WaNut.Core.Diagnostics;
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
using WaNut.Core.Profiling;
using WaNut.Core.Remote;
using WaNut.Core.Remote.Run;
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
    private CrashGuard? _crashGuard;

    public App()
    {
        _theme = new ThemeService(this);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RenderFallback.Apply();
        MotionSettings.Apply(this, SystemParameters.ClientAreaAnimation);
        var paths = LauncherPaths.CreateDefault();
        _crashGuard = new CrashGuard(new ErrorLog(paths.ErrorLogFile));
        _crashGuard.Install(this);
        var store = new AppSettingsStore(paths);
        var settings = store.Load();
        WaNut.App.Resources.UiLanguage.Apply(settings.Language);
        _theme.Start(settings.Theme);
        if (!settings.SetupDone)
        {
            // The setup window opens before the main window, so closing it must not end the app.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            new SetupWindow(new SetupViewModel(paths, store)).ShowDialog();
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Setup saves the servers folder the user picked.
            settings = store.Load();
        }

        _services = ConfigureServices(paths.WithInstancesDir(settings.InstancesDir), _theme, _crashGuard);
        _services.GetRequiredService<LauncherPaths>().EnsureCreated();

        var mainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
        _crashGuard.KeepRunning(_services.GetRequiredService<IDialogService>().Inform);

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

    private static ServiceProvider ConfigureServices(LauncherPaths paths, ThemeService theme, CrashGuard crashGuard)
    {
        var services = new ServiceCollection();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        services.AddSingleton(paths);
        services.AddSingleton(theme);
        services.AddSingleton(crashGuard);
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
        services.AddSingleton<VpsLaunchPlanner>();
        services.AddSingleton<IServerRuntime, LocalServerRuntime>();
        services.AddSingleton<ISecretProtector>(_ => new DpapiProtector());
        services.AddSingleton<SecretStore>();
        services.AddSingleton<VpsConnector>();
        services.AddSingleton<VpsSettingsSync>();
        services.AddSingleton<VpsPackHost>();
        services.AddSingleton<VpsTester>();
        services.AddSingleton(sp => new DiscordCheck(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<IBridgeBinarySource>(sp => new BridgeBinarySource(sp.GetRequiredService<LauncherPaths>()));
        services.AddSingleton<ILocalBridgeHost, WindowsBridgeHost>();
        services.AddSingleton<IBridgeDeployer>(sp => new LocalBridgeDeployer(
            sp.GetRequiredService<LauncherPaths>(),
            sp.GetRequiredService<InstanceStore>(),
            sp.GetRequiredService<IBridgeBinarySource>(),
            sp.GetRequiredService<DiscordCheck>(),
            sp.GetRequiredService<ILocalBridgeHost>(),
            new DpapiProtector(BridgeSecrets.Entropy)));
        services.AddSingleton<IBridgeDeployer>(sp => new SshVpsBridgeDeployer(
            sp.GetRequiredService<VpsConnector>(),
            id => sp.GetRequiredService<AppSettingsStore>().Load().Vps.FirstOrDefault(v => v.Id == id),
            sp.GetRequiredService<InstanceStore>(),
            sp.GetRequiredService<VpsSettingsSync>(),
            sp.GetRequiredService<IBridgeBinarySource>(),
            sp.GetRequiredService<DiscordCheck>()));
        services.AddSingleton<BridgeDeployerFactory>();
        services.AddSingleton(sp => new BridgeSettingsStore(sp.GetRequiredService<LauncherPaths>(), new DpapiProtector(BridgeSecrets.Entropy)));
        services.AddSingleton<BridgeManager>();
        services.AddSingleton<ServerHost>();
        services.AddSingleton<AppSettingsStore>();
        services.AddSingleton<BackupStore>();
        services.AddSingleton<MojangProfiles>();
        services.AddSingleton<PlayerSkins>();
        services.AddSingleton<PlayerFaces>();
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
        services.AddSingleton<WaNut.Core.Packs.PackLibrary>();
        services.AddSingleton<Spark>();
        services.AddSingleton<ModpackInstaller>();
        services.AddSingleton<DiscordWebhook>();
        services.AddSingleton<DiscordNotifier>();
        services.AddSingleton<PresenceService>();
        services.AddSingleton<BackupCopyService>();
        services.AddSingleton<TrayIcon>();

        // TrayIcon needs ServerController, which needs the notifier: the icon is fetched when a notification is shown.
        services.AddSingleton(sp => new DesktopNotifier(sp.GetRequiredService<TrayIcon>));

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ServerController>();
        services.AddSingleton<AppSettingsViewModel>();
        services.AddSingleton<UpdateLog>();
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
        services.AddTransient<DeleteServerPanel>();
        services.AddTransient<DuplicateServerViewModel>();
        services.AddTransient<DuplicateServerPanel>();
        services.AddTransient<VpsViewModel>();
        services.AddTransient<VpsWindow>();

        return services.BuildServiceProvider();
    }
}
