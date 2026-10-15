using System.Text.Json;
using WaNut.Core.Instances;

namespace WaNut.Core.Settings;

/// <summary>WaNut's own settings, stored in config.json.</summary>
public sealed record AppSettings
{
    /// <summary>Shows the advanced-only items, such as custom JVM arguments.</summary>
    public bool AdvancedMode { get; init; }
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
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(paths.ConfigFile), JsonOptions) ?? new AppSettings();
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
