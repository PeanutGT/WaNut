using System.Text.Json;
using WaNut.Core.Backups;
using WaNut.Core.Instances;

namespace WaNut.Core.Settings;

/// <summary>WaNut's own settings, stored in config.json.</summary>
public sealed record AppSettings
{
    /// <summary>Shows the advanced-only items, such as custom JVM arguments.</summary>
    public bool AdvancedMode { get; init; }

    /// <summary>Backup cleanup rules, shared by every server.</summary>
    public BackupRules Backups { get; init; } = new();

    /// <summary>Looks for a new WaNut release on GitHub each time WaNut starts.</summary>
    public bool CheckForUpdates { get; init; } = true;
}

public sealed class AppSettingsStore(LauncherPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>The saved settings, or the defaults when config.json is missing or damaged.</summary>
    public AppSettings Load()
    {
        if (!File.Exists(paths.ConfigFile))
        {
            return new AppSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(paths.ConfigFile), JsonOptions) ?? new AppSettings();
            return settings with { Backups = (settings.Backups ?? new BackupRules()).Normalized() };
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(paths.Root);
        AtomicFile.WriteAllText(paths.ConfigFile, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
