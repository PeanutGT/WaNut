using Velopack;
using WaNut.App.Services;
using WaNut.Core;
using WaNut.Core.Hosting;
using WaNut.Core.Updates;

namespace WaNut.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: during install, update and uninstall Velopack starts WaNut with special arguments, and
        // this handles them and exits before any window opens.
        VelopackApp.Build()
            .SetLogger(new VelopackLogAdapter(new UpdateLog(LauncherPaths.CreateDefault(), TimeProvider.System)))
            .OnBeforeUninstallFastCallback(_ => DesktopShortcut.Remove())
            .Run();

        // A second WaNut would fight the first over the servers folder and the tray icon: ask the first one to come
        // forward and leave.
        using var instance = SingleInstanceGuard.TryAcquire("WaNut.SingleInstance");
        if (instance is null)
        {
            return;
        }

        var app = new App { Instance = instance };
        app.InitializeComponent();
        app.Run();
    }
}
