using System.Windows;
using System.Windows.Automation;

namespace WaNut.App.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string message)
    {
        InitializeComponent();
        MessageText.Text = message;

        // Screen readers announce the question when the dialog opens.
        AutomationProperties.SetHelpText(this, message);
        Loaded += (_, _) => NoButton.Focus();
    }

    private void OnYes(object sender, RoutedEventArgs e) => DialogResult = true;
}
