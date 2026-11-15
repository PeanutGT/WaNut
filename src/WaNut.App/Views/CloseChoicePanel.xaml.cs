using System.Windows;
using System.Windows.Controls;
using WaNut.App.Controls;
using WaNut.Core.Settings;

namespace WaNut.App.Views;

public partial class CloseChoicePanel : UserControl
{
    public CloseChoicePanel(string serverName)
    {
        InitializeComponent();
        QuestionText.Text = string.Format(WaNut.App.Resources.Text.Get("Close_Question"), serverName);
    }

    /// <summary>Tray or Stop, or null when the user cancelled.</summary>
    public CloseAction? Choice { get; private set; }

    public bool Remember => RememberBox.IsChecked == true;

    private void OnTray(object sender, RoutedEventArgs e) => Choose(CloseAction.Tray);

    private void OnStop(object sender, RoutedEventArgs e) => Choose(CloseAction.Stop);

    private void Choose(CloseAction choice)
    {
        Choice = choice;
        InlinePanel.Close(this, answer: true);
    }
}
