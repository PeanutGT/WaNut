namespace WaNut.Core.Worlds;

/// <summary>A world that can be imported: a folder with level.dat, or a zip that holds one.</summary>
/// <param name="Path">The world folder, or the zip file.</param>
/// <param name="ZipRoot">For a zip, the folder inside it that holds level.dat ("" at the top, or e.g. "My World/"); null for a folder.</param>
/// <param name="Location">The launcher instance it was found in; null when the user picked it.</param>
/// <param name="ModFiles">The mods of that instance, for a modded world.</param>
public sealed record WorldSource(string Path, string? ZipRoot, WorldDetails Details, SaveLocation? Location, IReadOnlyList<string> ModFiles)
{
    public bool IsZip => ZipRoot is not null;
}
