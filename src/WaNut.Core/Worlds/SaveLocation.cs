namespace WaNut.Core.Worlds;

/// <summary>A "saves" folder of one launcher instance.</summary>
/// <param name="InstanceName">The instance's name in its launcher; null for the official launcher.</param>
/// <param name="GameDir">The instance's game folder, which holds saves/ and mods/.</param>
public sealed record SaveLocation(LauncherKind Launcher, string? InstanceName, string GameDir)
{
    public string SavesDir => Path.Combine(GameDir, "saves");

    /// <summary>The mod jars installed in this instance, by file name. Disabled mods are left out.</summary>
    public IReadOnlyList<string> ModFiles()
    {
        var mods = Path.Combine(GameDir, "mods");
        try
        {
            return Directory.Exists(mods)
                ? Directory.EnumerateFiles(mods, "*.jar").Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList()
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
