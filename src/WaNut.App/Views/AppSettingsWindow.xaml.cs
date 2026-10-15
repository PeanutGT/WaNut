using System.Windows;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class AppSettingsWindow : Window
{
    public AppSettingsWindow(AppSettingsViewModel viewModel, UpdateViewModel updates, ServersFolderViewModel serversFolder)
    {
        InitializeComponent();
        DataContext = viewModel;
        UpdatePanel.DataContext = updates;
        ServersFolderPanel.DataContext = serversFolder;
    }
}
