using System.Windows.Controls;
using System.Windows.Input;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class ServerFilesView : UserControl
{
    public ServerFilesView()
    {
        InitializeComponent();

        // The tab's content exists from the start; it counts as shown when it becomes visible.
        IsVisibleChanged += (_, e) =>
        {
            if (DataContext is not ServerFilesViewModel files)
            {
                return;
            }

            if (e.NewValue is true)
            {
                files.OnShown();
            }
            else
            {
                files.OnHidden();
            }
        };
        DataContextChanged += (_, e) =>
        {
            if (IsVisible && e.NewValue is ServerFilesViewModel files)
            {
                files.OnShown();
            }
        };
    }

    /// <summary>A double click opens or closes a folder, like the arrow does.</summary>
    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ServerFilesViewModel { SelectedIsDirectory: true } files)
        {
            files.ToggleSelectedCommand.Execute(null);
        }
    }
}
