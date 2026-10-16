using System.Windows;
using System.Windows.Automation;

namespace WaNut.App.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string message, bool isQuestion = true)
    {
        InitializeComponent();
        MessageText.Text = message;
        if (!isQuestion)
        {
            // A message to read, not a question: one Close button.
            YesButton.Visibility = Visibility.Collapsed;
            NoButton.Content = WaNut.App.Resources.Text.Get("Common_Close");
        }

        // Screen readers announce the question when the dialog opens.
        AutomationProperties.SetHelpText(this, message);
        Loaded += (_, _) => NoButton.Focus();
    }

    private void OnYes(object sender, RoutedEventArgs e) => DialogResult = true;
}
