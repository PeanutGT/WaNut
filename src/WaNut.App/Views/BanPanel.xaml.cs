using System.Windows.Controls;

namespace WaNut.App.Views;

/// <summary>Asks before banning a player and takes the reason they see (empty for none).</summary>
public partial class BanPanel : UserControl
{
    public BanPanel(string question)
    {
        InitializeComponent();
        QuestionText.Text = question;
    }

    public string Reason => ReasonBox.Text.Trim();
}
