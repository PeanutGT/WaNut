namespace WaNut.Core.Modpacks;

/// <summary>Paths inside a pack come from its author, so each one must stay inside the server folder.</summary>
public static class ModpackPaths
{
    /// <summary>
    /// The full path of <paramref name="relative"/> inside <paramref name="root"/>, or null when it is absolute,
    /// climbs out with "..", or is not a plain file path.
    /// </summary>
    public static string? Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains(':') || relative.StartsWith('/') || relative.StartsWith('\\'))
        {
            return null;
        }

        var parts = relative.Split('/', '\\');
        if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || p.Trim() != p || p.EndsWith('.')))
        {
            return null;
        }

        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine([fullRoot, .. parts]));
        var prefix = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
