using System.Windows;

namespace WaNut.App.Views;

/// <summary>Asks before a world reset and takes the seed for the new world (empty for a random one).</summary>
public partial class ResetWorldWindow : Window
{
    public ResetWorldWindow(string question)
    {
        InitializeComponent();
        QuestionText.Text = question;
        Loaded += (_, _) => CancelButton.Focus();
    }

    public string Seed => SeedBox.Text.Trim();

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
