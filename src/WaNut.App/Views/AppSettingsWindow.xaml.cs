using System.Windows;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class AppSettingsWindow : Window
{
    public AppSettingsWindow(AppSettingsViewModel viewModel, UpdateViewModel updates)
    {
        InitializeComponent();
        DataContext = viewModel;
        UpdatePanel.DataContext = updates;
    }
}
