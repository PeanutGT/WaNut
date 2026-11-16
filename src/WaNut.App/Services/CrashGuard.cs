using System.Windows;
using System.Windows.Threading;
using WaNut.App.Resources;
using WaNut.Core.Diagnostics;

namespace WaNut.App.Services;

/// <summary>
/// Keeps WaNut open when an exception nobody caught reaches the top: closing would take the servers on this PC down
/// with it, without a "stop" and a save. The error goes to <see cref="ErrorLog"/> and the user is told once.
/// </summary>
public sealed class CrashGuard(ErrorLog log)
{
    /// <summary>The same error again within this time is logged but not shown, so a repeating one can't bury the window.</summary>
    private static readonly TimeSpan QuietFor = TimeSpan.FromSeconds(30);

    private Action<string>? _show;
    private bool _showing;
    private string? _lastShown;
    private DateTime _lastShownAt;

    public void Install(Application app)
    {
        app.DispatcherUnhandledException += OnDispatcherException;

        // Tasks nobody awaited ("_ = XAsync()"): they can't close WaNut, but without this their errors vanish.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Write("background task", e.Exception);
            e.SetObserved();
        };

        // Off the UI thread nothing can stop the process from ending; at least say why.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error)
            {
                log.Write("fatal", error);
            }
        };
    }

    /// <summary>
    /// From here on UI-thread errors are shown with <paramref name="show"/> and WaNut carries on. Before the main
    /// window is up there is nothing to keep alive, so a startup error still ends WaNut instead of leaving it
    /// running without a window.
    /// </summary>
    public void KeepRunning(Action<string> show) => _show = show;

    /// <summary>Notes an error that was caught on purpose but is worth finding later.</summary>
    public void Note(string source, Exception error) => log.Write(source, error);

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        log.Write("window", e.Exception);
        if (_show is not { } show)
        {
            return;
        }

        e.Handled = true;
        var text = $"{e.Exception.GetType().FullName}: {e.Exception.Message}";
        if (_showing || (text == _lastShown && DateTime.UtcNow - _lastShownAt < QuietFor))
        {
            return;
        }

        _showing = true;
        try
        {
            show(string.Format(Text.Get("App_UnexpectedError"), e.Exception.Message, log.File));
        }
        catch (Exception showError)
        {
            // Telling the user failed too; staying open still matters more than the message.
            log.Write("window (showing the error)", showError);
        }
        finally
        {
            _showing = false;
            _lastShown = text;
            _lastShownAt = DateTime.UtcNow;
        }
    }
}
