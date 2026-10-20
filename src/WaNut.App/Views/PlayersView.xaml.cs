using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WaNut.App.Views;

public partial class PlayersView : UserControl
{
    public PlayersView()
    {
        InitializeComponent();
    }

    /// <summary>Opens a player's ⋯ menu under its button on a left click, not only on a right click.</summary>
    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }
}
