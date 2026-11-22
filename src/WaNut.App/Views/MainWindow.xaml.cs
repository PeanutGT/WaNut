using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WaNut.App.Controls;
using WaNut.App.Services;
using WaNut.App.ViewModels;
using WaNut.Core.Onboarding;
using WaNut.Core.Settings;

namespace WaNut.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TrayIcon _tray;
    private readonly CrashGuard _crashGuard;
    private bool _readyToClose;

    // Set by "Exit WaNut" in the tray menu: stop the server and close, without asking about the tray again.
    private bool _exitRequested;

    public MainWindow(MainViewModel viewModel, TrayIcon tray, CrashGuard crashGuard)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _tray = tray;
        _crashGuard = crashGuard;
        GuideLayer.FindTarget = FindGuideTarget;
        viewModel.TabRequested += (_, tab) => ServerTabs.SelectedIndex = (int)tab;
        Loaded += (_, _) =>
        {
            viewModel.StartPendingGuide();
            viewModel.ReconnectVpsServers();
        };
        // Signing out or shutting down must not wait for an answer to "keep running?".
        Application.Current.SessionEnding += (_, _) => _exitRequested = true;
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
        if (_readyToClose)
        {
            return;
        }

        if (_viewModel.IsShuttingDown)
        {
            e.Cancel = true;
            return;
        }

        // The close button hides WaNut in the tray unless the user said to exit, or Windows is signing out.
        if (!_exitRequested)
        {
            e.Cancel = true;
            var action = AskCloseAction();
            if (action == CloseAction.Tray)
            {
                Hide();
                _tray.Show();
                return;
            }

            if (action == CloseAction.Ask)
            {
                return;
            }
        }

        // Nothing runs here and nothing is syncing back: close right away.
        if (!_viewModel.NeedsShutdown)
        {
            e.Cancel = false;
            return;
        }

        // Never close with the server still running: send "stop" and let it save (VPS servers are let go of).
        e.Cancel = true;
        await ShutDownAndCloseAsync();
    }

    /// <summary>
    /// A failed shutdown must not leave a window that can never be closed. Once no server runs on this PC there is
    /// nothing left to protect, so WaNut closes anyway; while one still runs the error is shown and the window stays.
    /// </summary>
    private async Task ShutDownAndCloseAsync()
    {
        try
        {
            await _viewModel.ShutdownAsync();
        }
        catch (Exception e) when (!_viewModel.HasRunningServer)
        {
            _crashGuard.Note("shutdown", e);
        }

        _readyToClose = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray.Dispose();
        base.OnClosed(e);
    }

    /// <summary>Tray, Stop (exit), or Ask when the user cancelled (the window stays open).</summary>
    private CloseAction AskCloseAction()
    {
        if (_viewModel.AppSettings.CloseAction != CloseAction.Ask)
        {
            return _viewModel.AppSettings.CloseAction;
        }

        var panel = new CloseChoicePanel(_viewModel.HasRunningServer ? _viewModel.RunningServerNames : null);
        if (!InlinePanel.Show(this, panel) || panel.Choice is not { } choice)
        {
            return CloseAction.Ask;
        }

        if (panel.Remember)
        {
            _viewModel.AppSettings.CloseAction = choice;
        }

        return choice;
    }

    private FrameworkElement? FindGuideTarget(GuideStop stop)
    {
        switch (stop.Target)
        {
            case GuideTarget.CreateServer:
                return EmptyCreateButton.IsVisible ? EmptyCreateButton : SidebarCreateButton;
            case GuideTarget.ServerList:
                return ServerList;
            case GuideTarget.AppSettings:
                return AppSettingsButton;
            case GuideTarget.StartStop:
                return StartStopPanel;
            case GuideTarget.Tab when stop.TabIndex < ServerTabs.Items.Count:
                ServerTabs.SelectedIndex = stop.TabIndex;
                return ServerTabs.Items[stop.TabIndex] as TabItem;
            default:
                // Back on the console once the tour has walked through the tabs.
                ServerTabs.SelectedIndex = 0;
                return null;
        }
    }

    /// <summary>Shows the window again from the tray or the taskbar and brings it to the front.</summary>
    public void ShowFromTray()
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
