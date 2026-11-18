namespace WaNut.Core.Storage;

/// <summary>Checks and normalizes the relative paths an <see cref="IServerFileSystem"/> takes.</summary>
public static class ServerPath
{
    /// <summary>
    /// Returns <paramref name="path"/> with '/' separators and no empty or '.' segments; "" means the workspace root.
    /// </summary>
    /// <exception cref="ArgumentException">The path is rooted, contains '..' or a NUL character, or is null.</exception>
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A path can't contain a NUL character.", nameof(path));
        }

        var unified = path.Replace('\\', '/');
        if (unified.StartsWith('/') || (unified.Length >= 2 && unified[1] == ':'))
        {
            throw new ArgumentException($"'{path}' must be relative to the server folder.", nameof(path));
        }

        var segments = unified.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => s != ".").ToList();
        if (segments.Contains(".."))
        {
            throw new ArgumentException($"'{path}' leaves the server folder.", nameof(path));
        }

        return string.Join('/', segments);
    }

    public static string Combine(string folder, string name) => folder.Length == 0 ? name : $"{folder}/{name}";
}
