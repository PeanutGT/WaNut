using System.Windows;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
