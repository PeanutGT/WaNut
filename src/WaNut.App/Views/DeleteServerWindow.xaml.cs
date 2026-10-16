using System.ComponentModel;
using System.Windows;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class DeleteServerWindow : Window
{
    public DeleteServerWindow(DeleteServerViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += deleted => DialogResult = deleted;

        // Cancel is the safe answer, so it has the focus and Enter picks it.
        Loaded += (_, _) => CancelButton.Focus();
    }

    public DeleteServerViewModel ViewModel { get; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (ViewModel.IsDeleting)
        {
            e.Cancel = true;
            ViewModel.CancelAndClose();
        }

        base.OnClosing(e);
    }
}
