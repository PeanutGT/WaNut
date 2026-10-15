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

    /// <summary>
    /// The java.exe files WaNut already asked about letting through Windows Firewall, so the question comes once
    /// per Java, not on every start. The Connect tab can still add the rule later.
    /// </summary>
    public IReadOnlyList<string> FirewallAsked { get; init; } = [];

    /// <summary>Looks for a new WaNut release on GitHub each time WaNut starts.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>True once the first-run setup window was finished, so it shows only once.</summary>
    public bool SetupDone { get; init; }

    /// <summary>Light, dark or following Windows.</summary>
    public ThemeChoice Theme { get; init; }

    /// <summary>The folder the user picked for servers, or null for the default place in the data folder.</summary>
    public string? InstancesDir { get; init; }
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
            return settings with { Backups = (settings.Backups ?? new BackupRules()).Normalized(), FirewallAsked = settings.FirewallAsked ?? [] };
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
