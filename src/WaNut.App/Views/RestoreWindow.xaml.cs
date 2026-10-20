using System.Windows;
using WaNut.App.Resources;
using WaNut.Core.Backups;

namespace WaNut.App.Views;

/// <summary>Asks before a restore and, when the backup holds more than one part, whether to bring back only one of them.</summary>
public partial class RestoreWindow : Window
{
    public RestoreWindow(string question, IReadOnlyList<WorldPart> parts)
    {
        InitializeComponent();
        QuestionText.Text = question;
        var choices = new List<KeyValuePair<WorldPart[], string>> { new(Enum.GetValues<WorldPart>(), Text.Get("Restore_PartAll")) };
        if (parts.Count > 1)
        {
            choices.AddRange(parts.Select(part => new KeyValuePair<WorldPart[], string>([part], Text.Get($"Restore_Part{part}"))));
        }

        PartBox.ItemsSource = choices;
        PartBox.SelectedIndex = 0;
        PartPanel.Visibility = parts.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => CancelButton.Focus();
    }

    /// <summary>The parts to restore; every part for the whole world.</summary>
    public IReadOnlyCollection<WorldPart> Parts => ((KeyValuePair<WorldPart[], string>)PartBox.SelectedItem).Key;

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
