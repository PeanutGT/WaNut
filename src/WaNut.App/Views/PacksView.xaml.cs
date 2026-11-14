using System.Windows.Controls;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class PacksView : UserControl
{
    public PacksView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is PacksViewModel packs)
            {
                packs.OnShown();
            }
        };
    }
}
