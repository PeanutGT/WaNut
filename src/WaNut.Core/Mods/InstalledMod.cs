namespace WaNut.Core.Mods;

/// <summary>
/// A file in the server's mods or plugins folder. Files WaNut installed carry their Modrinth project; files the user
/// copied in by hand have only a file name.
/// </summary>
public sealed record InstalledMod(string FileName, string? ProjectId = null, string? VersionId = null, string? Title = null)
{
    public string DisplayName => Title ?? FileName;
}
