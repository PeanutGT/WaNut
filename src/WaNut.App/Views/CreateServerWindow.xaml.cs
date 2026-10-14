using System.ComponentModel;
using System.Windows;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class CreateServerWindow : Window
{
    public CreateServerWindow(CreateServerViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += created => DialogResult = created;
        Loaded += async (_, _) => await viewModel.LoadVersionsCommand.ExecuteAsync(null);
    }

    public CreateServerViewModel ViewModel { get; }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing mid-download would leave the user unsure what happened; stop the download first.
        if (ViewModel.IsCreating)
        {
            e.Cancel = true;
            ViewModel.CancelCreation();
        }

        base.OnClosing(e);
    }
}
