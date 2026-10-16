using System.Windows;
using WaNut.Core.Settings;

namespace WaNut.App.Views;

public partial class CloseChoiceWindow : Window
{
    public CloseChoiceWindow(string serverName)
    {
        InitializeComponent();
        QuestionText.Text = string.Format(WaNut.App.Resources.Text.Get("Close_Question"), serverName);
        Loaded += (_, _) => TrayButton.Focus();
    }

    /// <summary>Tray or Stop, or null when the user cancelled.</summary>
    public CloseAction? Choice { get; private set; }

    public bool Remember => RememberBox.IsChecked == true;

    private void OnTray(object sender, RoutedEventArgs e)
    {
        Choice = CloseAction.Tray;
        DialogResult = true;
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        Choice = CloseAction.Stop;
        DialogResult = true;
    }
}
