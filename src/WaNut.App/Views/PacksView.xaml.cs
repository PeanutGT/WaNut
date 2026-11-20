using System.Windows;
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

    /// <summary>Resource pack zips from Explorer can be dropped while the server is stopped and nothing else is changing.</summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && DataContext is PacksViewModel { CanDrop: true } ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && DataContext is PacksViewModel packs)
        {
            e.Handled = true;
            _ = packs.AddLocalResourcePacksAsync(files);
        }
    }
}
