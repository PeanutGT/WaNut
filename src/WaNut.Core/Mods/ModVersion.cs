namespace WaNut.Core.Mods;

/// <summary>A downloadable version of a Modrinth project: its main file and the projects it needs.</summary>
public sealed record ModVersion(
    string ProjectId,
    string VersionId,
    string VersionNumber,
    string FileName,
    Uri Url,
    string Sha512,
    IReadOnlyList<string> RequiredProjectIds);
