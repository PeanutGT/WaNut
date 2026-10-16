using System.Windows.Controls;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class ModsView : UserControl
{
    public ModsView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is ModsViewModel mods)
            {
                mods.OnShown();
            }
        };
    }
}
