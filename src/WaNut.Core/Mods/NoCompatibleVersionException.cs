namespace WaNut.Core.Mods;

/// <summary>The project has no version for this server's loader and Minecraft version.</summary>
public sealed class NoCompatibleVersionException(string title)
    : Exception($"{title} has no version for this server.")
{
    public string Title { get; } = title;
}
