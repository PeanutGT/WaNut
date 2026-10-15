using System.ComponentModel;
using System.Windows;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class ChangeServerWindow : Window
{
    public ChangeServerWindow(ChangeServerViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += changed => DialogResult = changed;
        Loaded += async (_, _) => await viewModel.LoadVersionsCommand.ExecuteAsync(null);
    }

    public ChangeServerViewModel ViewModel { get; }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing mid-change would leave the user unsure what happened: the window stays until the change has
        // stopped (or, while files are swapped, finished), then closes by itself.
        if (ViewModel.IsChanging)
        {
            e.Cancel = true;
            ViewModel.CancelAndClose();
        }

        base.OnClosing(e);
    }
}
