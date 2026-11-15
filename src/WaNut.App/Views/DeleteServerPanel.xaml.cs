using System.Windows.Controls;
using WaNut.App.Controls;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class DeleteServerPanel : UserControl, InlinePanel.IDismissHandler
{
    public DeleteServerPanel(DeleteServerViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += deleted => InlinePanel.Close(this, deleted);
    }

    public DeleteServerViewModel ViewModel { get; }

    public void Dismiss(bool byEscape)
    {
        if (byEscape)
        {
            // Like the Cancel button: stops a delete under way and stays open, or closes when idle.
            ViewModel.CancelCommand.Execute(null);
        }
        else if (ViewModel.IsDeleting)
        {
            ViewModel.CancelAndClose();
        }
        else
        {
            InlinePanel.Close(this, answer: false);
        }
    }
}
