using System.Windows.Controls;
using WaNut.App.Resources;
using WaNut.App.ViewModels;
using WaNut.Core.Settings;

namespace WaNut.App.Views;

/// <summary>Asks before a world reset and takes the seed (empty for a random one) and the terrain type for the new world.</summary>
public partial class ResetWorldPanel : UserControl
{
    public ResetWorldPanel(string question)
    {
        InitializeComponent();
        QuestionText.Text = question;
        TypeBox.ItemsSource = Enum.GetValues<WorldType>().Select(t => new Option<WorldType>(t, Text.Get($"CreateServer_WorldType{t}"))).ToList();
        TypeBox.SelectedValue = WorldType.Normal;
    }

    public WorldType WorldType => TypeBox.SelectedValue is WorldType type ? type : WorldType.Normal;

    public string Seed => SeedBox.Text.Trim();
}
