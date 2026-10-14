namespace WaNut.Core;

/// <summary>
/// Locations of WaNut's data folders. Everything lives under one root so servers never conflict.
/// </summary>
public sealed class LauncherPaths
{
    public const string AppFolderName = "WaNut";

    /// <summary>Points WaNut at another data folder, for testing without touching the real servers.</summary>
    public const string DataDirVariable = "WANUT_DATA_DIR";

    public LauncherPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        Root = root;
        RuntimesDir = Path.Combine(root, "runtimes");
        CacheDir = Path.Combine(root, "cache");
        InstancesDir = Path.Combine(root, "instances");
        ConfigFile = Path.Combine(root, "config.json");
    }

    public string Root { get; }

    public string RuntimesDir { get; }

    public string CacheDir { get; }

    public string InstancesDir { get; }

    public string ConfigFile { get; }

    public static LauncherPaths CreateDefault() => CreateDefault(Environment.GetEnvironmentVariable(DataDirVariable));

    /// <summary>%APPDATA%\WaNut, unless <paramref name="dataDirOverride"/> names another folder.</summary>
    public static LauncherPaths CreateDefault(string? dataDirOverride)
    {
        if (!string.IsNullOrWhiteSpace(dataDirOverride))
        {
            return new LauncherPaths(Path.GetFullPath(dataDirOverride));
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new LauncherPaths(Path.Combine(appData, AppFolderName));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RuntimesDir);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(InstancesDir);
    }
}
