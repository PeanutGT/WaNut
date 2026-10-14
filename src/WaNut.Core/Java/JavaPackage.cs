namespace WaNut.Core.Java;

/// <summary>A portable Temurin JRE zip for Windows x64.</summary>
public sealed record JavaPackage(int Major, string ReleaseName, Uri Link, string Sha256, long Size);
