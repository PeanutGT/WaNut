using System.ComponentModel;
using System.Windows;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _readyToClose;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_readyToClose || !_viewModel.HasRunningServer)
        {
            return;
        }

        // Never close with the server still running: send "stop", let it save the world, then close.
        e.Cancel = true;
        if (_viewModel.IsShuttingDown)
        {
            return;
        }

        await _viewModel.ShutdownAsync();
        _readyToClose = true;
        Close();
    }
}
