using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace WaNut.App.Views;

public partial class ConfirmPanel : UserControl
{
    public ConfirmPanel(string message, bool isQuestion = true)
    {
        InitializeComponent();
        MessageText.Text = message;
        if (!isQuestion)
        {
            // A message to read, not a question: one Close button.
            YesButton.Visibility = Visibility.Collapsed;
            NoButton.Content = WaNut.App.Resources.Text.Get("Common_Close");
        }

        // Screen readers announce the question when the panel opens and focus lands on its button.
        AutomationProperties.SetHelpText(NoButton, message);
    }
}
