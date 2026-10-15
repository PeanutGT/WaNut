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
        // Closing mid-change would leave the user unsure what happened: stop downloads first, and never stop while files are swapped.
        if (ViewModel.IsChanging)
        {
            e.Cancel = true;
            ViewModel.CancelChange();
        }

        base.OnClosing(e);
    }
}
