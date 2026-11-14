namespace WaNut.Core.Mods;

/// <summary>What the details window shows about a Modrinth project.</summary>
/// <param name="Body">The long description, in Markdown.</param>
/// <param name="Gallery">HTTPS pictures, featured ones first.</param>
/// <param name="ClientSide">"required", "optional" or "unsupported": whether players need it in their game too.</param>
/// <param name="ServerSide">"required", "optional" or "unsupported".</param>
public sealed record ModProjectDetails(
    string Id,
    string Title,
    string Description,
    string Body,
    Uri? IconUrl,
    IReadOnlyList<Uri> Gallery,
    string? ClientSide,
    string? ServerSide);
