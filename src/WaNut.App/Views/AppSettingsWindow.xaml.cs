using System.Windows;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class AppSettingsWindow : Window
{
    private readonly AppSettingsViewModel _viewModel;

    public AppSettingsWindow(AppSettingsViewModel viewModel, UpdateViewModel updates, ServersFolderViewModel serversFolder)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        UpdatePanel.DataContext = updates;
        ServersFolderPanel.DataContext = serversFolder;
    }

    // The tour runs on the main window, so this window gets out of the way first.
    private void OnShowGuide(object sender, RoutedEventArgs e)
    {
        Close();
        _viewModel.RequestGuide();
    }
}
