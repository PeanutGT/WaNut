using System.Windows.Controls;

namespace WaNut.App.Views;

/// <summary>Asks before a world reset and takes the seed for the new world (empty for a random one).</summary>
public partial class ResetWorldPanel : UserControl
{
    public ResetWorldPanel(string question)
    {
        InitializeComponent();
        QuestionText.Text = question;
    }

    public string Seed => SeedBox.Text.Trim();
}
