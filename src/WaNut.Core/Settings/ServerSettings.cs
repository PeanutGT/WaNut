using System.Globalization;
using WaNut.Core.Properties;

namespace WaNut.Core.Settings;

/// <summary>The server.properties settings on the Server tab.</summary>
public sealed record ServerSettings
{
    public const int DefaultPort = 25565;

    public int Port { get; init; } = DefaultPort;

    /// <summary>Checks each player's Minecraft account with Mojang. Off lets anyone join under any name.</summary>
    public bool OnlineMode { get; init; } = true;

    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;
}

public static class ServerSettingsStore
{
    public static ServerSettings Load(string instanceDir)
    {
        var properties = PropertiesFile.Load(Path.Combine(instanceDir, GameSettingsStore.PropertiesFileName));
        var defaults = new ServerSettings();
        var port = int.TryParse(properties.Get("server-port")?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && ServerSettings.IsValidPort(p)
            ? p
            : defaults.Port;
        var onlineMode = properties.Get("online-mode")?.Trim().ToLowerInvariant() switch
        {
            "false" => false,
            "true" => true,
            _ => defaults.OnlineMode,
        };
        return new ServerSettings { Port = port, OnlineMode = onlineMode };
    }

    /// <summary>Writes only what changed, like <see cref="GameSettingsStore.Save"/>.</summary>
    public static void Save(string instanceDir, ServerSettings loaded, ServerSettings edited)
    {
        if (!ServerSettings.IsValidPort(edited.Port))
        {
            throw new ArgumentOutOfRangeException(nameof(edited), edited.Port, "Port must be 1-65535.");
        }

        var path = Path.Combine(instanceDir, GameSettingsStore.PropertiesFileName);
        var properties = PropertiesFile.Load(path);
        var changed = false;
        if (loaded.Port != edited.Port)
        {
            properties.Set("server-port", edited.Port.ToString(CultureInfo.InvariantCulture));
            changed = true;
        }

        if (loaded.OnlineMode != edited.OnlineMode)
        {
            properties.Set("online-mode", edited.OnlineMode ? "true" : "false");
            changed = true;
        }

        if (changed)
        {
            properties.Save(path);
        }
    }
}
