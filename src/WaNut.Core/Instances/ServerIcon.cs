namespace WaNut.Core.Instances;

/// <summary>The picture friends see next to the server in Minecraft's server list.</summary>
public static class ServerIcon
{
    /// <summary>Minecraft reads this file from the server folder at start. It must be a 64x64 PNG.</summary>
    public const string FileName = "server-icon.png";

    public const int Size = 64;

    public static string PathIn(string instanceDir) => Path.Combine(instanceDir, FileName);
}
