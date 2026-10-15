using System.Windows.Controls;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class BackupsView : UserControl
{
    public BackupsView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is BackupsViewModel backups)
            {
                backups.OnShown();
            }
        };
    }
}
