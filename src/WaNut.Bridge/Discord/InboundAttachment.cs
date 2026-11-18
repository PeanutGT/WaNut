namespace WaNut.Bridge.Discord;

/// <summary>A file attached to a Discord message; <paramref name="Url"/> is Discord's own CDN link.</summary>
public sealed record InboundAttachment(string Url, string FileName, string? ContentType, long Size)
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    public bool IsImage =>
        ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
        || ImageExtensions.Any(e => FileName.EndsWith(e, StringComparison.OrdinalIgnoreCase));
}
