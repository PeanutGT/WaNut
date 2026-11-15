using System.Windows.Controls;
using WaNut.App.Controls;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class DuplicateServerPanel : UserControl, InlinePanel.IDismissHandler
{
    public DuplicateServerPanel(DuplicateServerViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += copied => InlinePanel.Close(this, copied);

        // The suggested name is selected, so typing replaces it.
        NameBox.GotKeyboardFocus += (_, _) => NameBox.SelectAll();
    }

    public DuplicateServerViewModel ViewModel { get; }

    public void Dismiss(bool byEscape)
    {
        if (byEscape)
        {
            // Like the Cancel button: stops a copy under way and stays open, or closes when idle.
            ViewModel.CancelCommand.Execute(null);
        }
        else if (ViewModel.IsCopying)
        {
            ViewModel.CancelAndClose();
        }
        else
        {
            InlinePanel.Close(this, answer: false);
        }
    }
}
