namespace WaNut.Core.Java;

/// <summary>A portable Temurin JRE: a zip for Windows x64, or a .tar.gz for a Linux VPS.</summary>
public sealed record JavaPackage(int Major, string ReleaseName, Uri Link, string Sha256, long Size);
