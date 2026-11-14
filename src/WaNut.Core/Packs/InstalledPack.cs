namespace WaNut.Core.Packs;

/// <summary>A data pack in the world's datapacks folder: a .zip or a folder. Packs WaNut installed carry their Modrinth project.</summary>
public sealed record InstalledPack(string FileName, string? ProjectId = null, string? Title = null)
{
    public string DisplayName => Title ?? FileName;
}
