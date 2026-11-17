using System.Windows.Controls;

namespace WaNut.App.Views;

public partial class ServerSettingsView : UserControl
{
    public ServerSettingsView()
    {
        InitializeComponent();

        // The tab's content exists from the start; it counts as shown when it becomes visible or gets another server.
        IsVisibleChanged += (_, _) => NotifyShown();
        DataContextChanged += (_, _) => NotifyShown();
    }

    private void NotifyShown()
    {
        if (IsVisible && DataContext is ViewModels.ServerSettingsViewModel viewModel)
        {
            viewModel.OnShown();
        }
    }
}
