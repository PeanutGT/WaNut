using System.ComponentModel;
using System.Windows;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class DuplicateServerWindow : Window
{
    public DuplicateServerWindow(DuplicateServerViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += copied => DialogResult = copied;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public DuplicateServerViewModel ViewModel { get; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (ViewModel.IsCopying)
        {
            e.Cancel = true;
            ViewModel.CancelAndClose();
        }

        base.OnClosing(e);
    }
}
