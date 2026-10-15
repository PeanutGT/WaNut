using Velopack;
using WaNut.App.Services;

namespace WaNut.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: during install, update and uninstall Velopack starts WaNut with special arguments, and
        // this handles them and exits before any window opens.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => DesktopShortcut.Remove())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
