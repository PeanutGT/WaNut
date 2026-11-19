namespace WaNut.Core.Hotplug;

/// <summary>
/// Which data pack file names can be hot-plugged. The name ends up in sh commands (quoted) and inside a quoted
/// Minecraft command argument ("file/&lt;name&gt;"), so anything that could end that argument or the console line is refused.
/// </summary>
public static class HotplugNames
{
    public const string Extension = ".zip";

    /// <summary>Why <paramref name="fileName"/> can't be hot-plugged, or null when it can.</summary>
    public static string? Problem(string fileName)
    {
        if (!fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || fileName.Length <= Extension.Length)
        {
            return "Only .zip data packs can be hot-plugged.";
        }

        if (fileName.StartsWith('.') || fileName.Any(c => char.IsControl(c) || c is '/' or '\\' or '"' or '[' or ']'))
        {
            return $"\"{Clean(fileName)}\" can't be hot-plugged: rename it without a leading dot, slashes, quotes or square brackets.";
        }

        return null;
    }

    /// <summary>The name Minecraft gives a pack file in the world's datapacks folder, as /datapack commands take it.</summary>
    public static string PackId(string fileName) => "file/" + fileName;

    /// <summary>A Minecraft command argument in double quotes; <see cref="Problem"/> already refused quotes and backslashes.</summary>
    public static string Quoted(string fileName) => "\"" + PackId(fileName) + "\"";

    private static string Clean(string value) => new(value.Where(c => !char.IsControl(c)).ToArray());
}
