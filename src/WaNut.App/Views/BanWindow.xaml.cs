using System.Windows;

namespace WaNut.App.Views;

/// <summary>Asks before banning a player and takes the reason they see (empty for none).</summary>
public partial class BanWindow : Window
{
    public BanWindow(string question)
    {
        InitializeComponent();
        QuestionText.Text = question;
        Loaded += (_, _) => CancelButton.Focus();
    }

    public string Reason => ReasonBox.Text.Trim();

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
