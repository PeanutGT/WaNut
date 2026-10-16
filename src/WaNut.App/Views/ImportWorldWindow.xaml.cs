using System.ComponentModel;
using System.Windows;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class ImportWorldWindow : Window
{
    public ImportWorldWindow(ImportWorldViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += imported => DialogResult = imported;
        Loaded += async (_, _) => await viewModel.Picker.ScanOnceAsync();
    }

    public ImportWorldViewModel ViewModel { get; }

    protected override void OnClosing(CancelEventArgs e)
    {
        // The copy stops and cleans up first, so closing never leaves the world half-replaced.
        if (ViewModel.IsImporting)
        {
            e.Cancel = true;
            ViewModel.CancelAndClose();
        }

        base.OnClosing(e);
    }
}
