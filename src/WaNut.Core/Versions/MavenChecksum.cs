namespace WaNut.Core.Versions;

/// <summary>Reads the .sha1 / .sha256 files Maven repositories publish next to each file.</summary>
public static class MavenChecksum
{
    public const int Sha1Length = 40;
    public const int Sha256Length = 64;

    /// <summary>The bare hex value, sometimes followed by a file name.</summary>
    public static string Parse(string text, int hexLength)
    {
        var hex = text.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (hex.Length != hexLength || !hex.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("A Maven repository returned an unreadable checksum.");
        }

        return hex.ToLowerInvariant();
    }
}
