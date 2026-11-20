namespace WaNut.Core.Packs;

/// <summary>What <see cref="ResourcePackLink.CheckAsync"/> found at the address.</summary>
public enum ResourcePackLinkStatus
{
    /// <summary>The download is the zip WaNut built.</summary>
    Match,

    /// <summary>A file, but not this zip: an older upload, or another file.</summary>
    Different,

    /// <summary>A web page, so the link is not a direct download.</summary>
    WebPage,

    /// <summary>No answer or an error status.</summary>
    Unreachable,
}

/// <param name="Status">The result.</param>
/// <param name="Detail">The error text for <see cref="ResourcePackLinkStatus.Unreachable"/>.</param>
public sealed record ResourcePackLinkCheck(ResourcePackLinkStatus Status, string? Detail = null);
