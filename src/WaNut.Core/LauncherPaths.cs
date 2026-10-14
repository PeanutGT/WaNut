namespace WaNut.Core;

/// <summary>
/// Locations of WaNut's data folders. Everything lives under one root so servers never conflict.
/// </summary>
public sealed class LauncherPaths
{
    public const string AppFolderName = "WaNut";

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

    public static LauncherPaths CreateDefault()
    {
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
