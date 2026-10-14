namespace WaNut.Core.Versions;

public sealed record VanillaServerInfo(string VersionId, Uri JarUrl, string JarSha1, long JarSize, int JavaMajor);
