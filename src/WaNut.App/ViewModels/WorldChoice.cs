namespace WaNut.App.ViewModels;

/// <summary>One choice in a short list of options, such as "New world" or "Import my world".</summary>
public sealed record WorldChoice(string Id, string Title, string Description);
