namespace WaNut.Core.Mods;

/// <summary>
/// Modrinth marks the project (or a dependency it would bring) and some installed mods as not working together.
/// Install again with allowIncompatible to go ahead anyway.
/// </summary>
public sealed class IncompatibleModsException(string title, IReadOnlyList<string> installed)
    : Exception($"{title} does not work together with {string.Join(", ", installed)}.")
{
    public string Title { get; } = title;

    /// <summary>The display names of the installed mods it clashes with.</summary>
    public IReadOnlyList<string> Installed { get; } = installed;
}
