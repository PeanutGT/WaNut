namespace WaNut.Core.Versions;

/// <summary>
/// A Minecraft release listed in Mojang's version manifest.
/// </summary>
public sealed record GameVersion(string Id, DateTimeOffset ReleaseTime, Uri DetailsUrl, string DetailsSha1)
{
    public override string ToString() => Id;
}
