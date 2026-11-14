namespace WaNut.Core.Mods;

/// <summary>
/// A downloadable version of a Modrinth project: its main file, the projects it needs, and the projects it says it
/// does not work with.
/// </summary>
public sealed record ModVersion(
    string ProjectId,
    string VersionId,
    string VersionNumber,
    string FileName,
    Uri Url,
    string Sha512,
    IReadOnlyList<string> RequiredProjectIds,
    IReadOnlyList<string> IncompatibleProjectIds)
{
    /// <summary>"release", "beta" or "alpha".</summary>
    public string VersionType { get; init; } = "release";

    /// <summary>When the version came out; null when Modrinth did not say.</summary>
    public DateTimeOffset? Published { get; init; }
}
