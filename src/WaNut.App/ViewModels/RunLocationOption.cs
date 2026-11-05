namespace WaNut.App.ViewModels;

/// <summary>One choice in the Server tab's "Where it runs": this PC (no VPS id) or one of the user's VPS hosts.</summary>
public sealed record RunLocationOption(string? VpsId, string Name)
{
    public override string ToString() => Name;
}
