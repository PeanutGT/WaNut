using WaNut.Core.Storage;

namespace WaNut.Core.Workspace;

/// <summary>Decides which files the editor opens: small settings-style text files, never jars, worlds or archives.</summary>
public static class TextFilePolicy
{
    /// <summary>Large enough for any real config (a big Paper config is a few hundred KB), small enough to read over a slow VPS link at once.</summary>
    public const long MaxBytes = 1024 * 1024;

    private static readonly string[] Extensions =
    [
        ".properties", ".yml", ".yaml", ".toml", ".json", ".json5", ".conf", ".cfg", ".txt", ".ini", ".xml", ".md", ".mcmeta", ".snbt", ".sh", ".log",
    ];

    /// <returns>null when the file can be opened.</returns>
    public static TextEditProblem? Check(ServerFileNode node)
    {
        if (node.IsDirectory)
        {
            return TextEditProblem.Folder;
        }

        var name = node.Name.ToLowerInvariant();
        if (!Extensions.Any(e => name.EndsWith(e, StringComparison.Ordinal)) || node.Category == McFileCategory.WorldData)
        {
            return TextEditProblem.NotText;
        }

        return node.SizeBytes > MaxBytes ? TextEditProblem.TooLarge : null;
    }

    /// <summary>Files the server rewrites all the time: viewing them is fine, but a saved edit would be lost.</summary>
    public static bool ServerOwned(ServerFileNode node) => node.Category == McFileCategory.LogOrCrash;
}
