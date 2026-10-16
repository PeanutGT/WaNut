namespace WaNut.Core.Mods;

/// <summary>
/// The titles of everything installed (the project first, then the dependencies it pulled in), and the required
/// dependencies that have no version for this server.
/// </summary>
public sealed record ModInstallResult(IReadOnlyList<string> Installed, IReadOnlyList<string> MissingDependencies);
