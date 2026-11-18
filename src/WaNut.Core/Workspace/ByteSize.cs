using System.Globalization;

namespace WaNut.Core.Workspace;

/// <summary>File sizes for people: "512 B", "3.4 KB", "1.2 GB". Small files must not read as "0.0 MB".</summary>
public static class ByteSize
{
    public static string Format(long bytes, IFormatProvider? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return bytes switch
        {
            < 1024 => string.Format(culture, "{0} B", Math.Max(bytes, 0)),
            < 1024 * 1024 => string.Format(culture, "{0:0.#} KB", bytes / 1024.0),
            < 1024L * 1024 * 1024 => string.Format(culture, "{0:0.#} MB", bytes / (1024.0 * 1024)),
            _ => string.Format(culture, "{0:0.##} GB", bytes / (1024.0 * 1024 * 1024)),
        };
    }
}
