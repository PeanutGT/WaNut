using System.Windows;
using System.Windows.Controls;

namespace WaNut.App.Views;

public partial class GameSettingsView : UserControl
{
    public GameSettingsView()
    {
        InitializeComponent();

        // The tab's content exists from the start; it counts as shown when it becomes visible or gets another server.
        IsVisibleChanged += (_, _) => NotifyShown();
        DataContextChanged += (_, _) => NotifyShown();
    }

    private void NotifyShown()
    {
        if (IsVisible && DataContext is ViewModels.GameSettingsViewModel viewModel)
        {
            viewModel.OnShown();
        }
    }

    private void Item_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (sender is FrameworkElement item && !ReferenceEquals(e.TargetObject, item))
        {
            e.Handled = true;
            item.BringIntoView();
        }
    }
}
