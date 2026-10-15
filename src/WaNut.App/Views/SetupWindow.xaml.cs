using System.Windows;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class SetupWindow : Window
{
    public SetupWindow(SetupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Finished += (_, _) =>
        {
            DialogResult = true;
        };
    }
}
