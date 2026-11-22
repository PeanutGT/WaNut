using System.Windows;
using System.Windows.Controls;
using WaNut.App.Controls;
using WaNut.Core.Settings;

namespace WaNut.App.Views;

public partial class CloseChoicePanel : UserControl
{
    /// <summary>The server name, or null when no server runs on this PC and the question is about WaNut itself.</summary>
    public CloseChoicePanel(string? serverName)
    {
        InitializeComponent();
        var text = WaNut.App.Resources.Text.Get;
        if (serverName is null)
        {
            QuestionText.Text = text("Close_QuestionIdle");
            HintText.Text = text("Close_HintIdle");
            StopButton.Content = text("Close_StopIdle");
        }
        else
        {
            QuestionText.Text = string.Format(text("Close_Question"), serverName);
            HintText.Text = text("Close_Hint");
            StopButton.Content = text("Close_Stop");
        }
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
