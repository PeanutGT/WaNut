using System.Windows;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class ModDetailsWindow : Window
{
    public ModDetailsWindow(ModDetailsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += picked => DialogResult = picked;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }

    public ModDetailsViewModel ViewModel { get; }
}
