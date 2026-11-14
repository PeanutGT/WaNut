namespace WaNut.Core.Packs;

/// <summary>A downloadable version of a Modrinth data pack or resource pack: its main .zip and checksums.</summary>
public sealed record PackVersion(
    string ProjectId,
    string VersionId,
    string VersionNumber,
    string FileName,
    Uri Url,
    string Sha512,
    string Sha1);
