namespace WaNut.Core.Versions;

/// <summary>A Forge or NeoForge installer for one loader version, with the checksum its Maven publishes.</summary>
/// <param name="LoaderVersion">The loader's own version, e.g. "47.3.0" for Forge or "21.1.209" for NeoForge.</param>
/// <param name="FullVersion">The Maven version: "1.20.1-47.3.0" for Forge, the same as the loader version for NeoForge.</param>
public sealed record ForgeInstaller(ServerLoader Loader, string LoaderVersion, string FullVersion, Uri JarUrl, string Sha1);
