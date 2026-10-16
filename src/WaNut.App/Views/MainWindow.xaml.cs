using System.ComponentModel;
using System.Windows;
using WaNut.App.Services;
using WaNut.App.ViewModels;
using WaNut.Core.Settings;

namespace WaNut.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TrayIcon _tray;
    private bool _readyToClose;

    // Set by "Exit WaNut" in the tray menu: stop the server and close, without asking about the tray again.
    private bool _exitRequested;

    public MainWindow(MainViewModel viewModel, TrayIcon tray)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _tray = tray;
        tray.OpenRequested += (_, _) => ShowFromTray();
        tray.ExitRequested += (_, _) =>
        {
            _exitRequested = true;
            ShowFromTray();
            Close();
        };
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_readyToClose || !_viewModel.HasRunningServer)
        {
            return;
        }

        // Never close with the server still running: keep it running in the tray, or send "stop" and let it save.
        e.Cancel = true;
        if (_viewModel.IsShuttingDown)
        {
            return;
        }

        if (!_exitRequested && AskCloseAction() is var action && action != CloseAction.Stop)
        {
            if (action == CloseAction.Tray)
            {
                Hide();
                _tray.Show();
            }

            return;
        }

        await _viewModel.ShutdownAsync();
        _readyToClose = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray.Dispose();
        base.OnClosed(e);
    }

    /// <summary>Tray, Stop, or Ask when the user cancelled (the window stays open).</summary>
    private CloseAction AskCloseAction()
    {
        if (_viewModel.AppSettings.CloseAction != CloseAction.Ask)
        {
            return _viewModel.AppSettings.CloseAction;
        }

        var window = new CloseChoiceWindow(_viewModel.RunningServerName ?? string.Empty) { Owner = this };
        if (window.ShowDialog() != true || window.Choice is not { } choice)
        {
            return CloseAction.Ask;
        }

        if (window.Remember)
        {
            _viewModel.AppSettings.CloseAction = choice;
        }

        return choice;
    }

    private void ShowFromTray()
    {
        _tray.Hide();
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }
}
