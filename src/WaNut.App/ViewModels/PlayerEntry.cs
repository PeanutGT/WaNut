using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WaNut.App.Resources;

namespace WaNut.App.ViewModels;

/// <summary>
/// A player in one of the Players tab's lists: their name and face, the text a screen reader says for the row's
/// button, and, for online and earlier players, which lists they are on.
/// </summary>
public sealed partial class PlayerEntry(string name, string actionLabel) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>What a screen reader says for the row's button: "Remove Alex", "Unban Alex" or "More actions for Alex".</summary>
    public string ActionLabel { get; } = actionLabel;

    /// <summary>The first letter, shown while the face loads or when the player has no skin.</summary>
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFace))]
    private ImageSource? _face;

    public bool HasFace => Face is not null;

    public bool IsOnline { get; set; }

    // Observable: a row stays when the lists change, so its ⋯ menu must follow.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperatorAction))]
    private bool _isOperator;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WhitelistAction))]
    private bool _isWhitelisted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BanAction))]
    private bool _isBanned;

    public string OperatorAction => Text.Get(IsOperator ? "Players_RemoveOperator" : "Players_MakeOperator");

    public string WhitelistAction => Text.Get(IsWhitelisted ? "Players_RemoveFromWhitelist" : "Players_AddToWhitelist");

    public string BanAction => Text.Get(IsBanned ? "Players_Unban" : "Players_Ban");

    // Screen readers read the row as the player's name.
    public override string ToString() => Name;
}
