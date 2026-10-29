namespace WaNut.Core.Mods;

/// <summary>The Modrinth project a file belongs to, and the projects its version says it does not work with.</summary>
public sealed record IdentifiedVersion(string ProjectId, IReadOnlyList<string> IncompatibleProjectIds);
