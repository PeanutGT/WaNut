using System.ComponentModel;
using System.Windows;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class ExportWorldWindow : Window
{
    public ExportWorldWindow(ExportWorldViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += exported => DialogResult = exported;
    }

    public ExportWorldViewModel ViewModel { get; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (ViewModel.IsExporting)
        {
            e.Cancel = true;
            ViewModel.CancelAndClose();
        }

        base.OnClosing(e);
    }
}
