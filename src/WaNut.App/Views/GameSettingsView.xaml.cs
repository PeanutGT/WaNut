using System.Windows;
using System.Windows.Controls;

namespace WaNut.App.Views;

public partial class GameSettingsView : UserControl
{
    public GameSettingsView()
    {
        InitializeComponent();
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
