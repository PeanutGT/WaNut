namespace WaNut.Core.Java;

public static class JavaVersionResolver
{
    /// <summary>
    /// Java version for a Minecraft version, used only when Mojang's own data does not say.
    /// </summary>
    public static int FallbackFor(string minecraftVersion)
    {
        var parts = minecraftVersion.Split('.');
        if (parts.Length >= 1 && int.TryParse(parts[0], out var year) && year >= 26)
        {
            // Year-based versions (26.x) ship with Java 25 in Mojang's data.
            return 25;
        }

        if (parts.Length < 2 || parts[0] != "1" || !int.TryParse(parts[1], out var minor))
        {
            return 21;
        }

        var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;
        return minor switch
        {
            <= 16 => 8,
            // 1.17 officially asks for Java 16; Java 17 runs it fine (blueprint 1.A).
            <= 19 => 17,
            20 when patch <= 4 => 17,
            _ => 21,
        };
    }
}
