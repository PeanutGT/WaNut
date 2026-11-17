using System.Text.Json;
using System.Text.Json.Serialization;
using WaNut.Core.Instances;
using WaNut.Core.Remote;

namespace WaNut.Core.Bridge;

/// <summary>
/// One file per server under %APPDATA%\WaNut\BridgeSettings\. The bot token and webhook URL are encrypted with the
/// same "dpapi:" scheme as the bridge's own config (<see cref="BridgeSecrets"/>), so they are never written as plain text here.
/// </summary>
public sealed class BridgeSettingsStore(LauncherPaths paths, ISecretProtector protector)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Folder { get; } = Path.Combine(paths.Root, "BridgeSettings");

    /// <summary>The saved settings, or null when none were saved, the file is damaged, or another Windows account saved it.</summary>
    public BridgeSettings? Load(string serverId)
    {
        var path = PathFor(serverId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<BridgeSettings>(File.ReadAllText(path), JsonOptions);
            return settings is null ? null : settings with { Config = BridgeSecrets.Unprotect(settings.Config, protector) };
        }
        catch (Exception ex) when (ex is JsonException or IOException or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public void Save(string serverId, BridgeSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var safe = settings with
        {
            Config = BridgeSecrets.Protect(settings.Config with { RconPassword = string.Empty, LogFile = string.Empty }, protector),
        };
        AtomicFile.WriteAllText(PathFor(serverId), JsonSerializer.Serialize(safe, JsonOptions));
    }

    /// <summary>Does nothing when nothing was saved.</summary>
    public void Delete(string serverId)
    {
        var path = PathFor(serverId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(string serverId)
    {
        // Ids are folder names WaNut made; anything else could step outside the folder.
        if (string.IsNullOrWhiteSpace(serverId) || serverId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || serverId is "." or "..")
        {
            throw new ArgumentException("Not a server id.", nameof(serverId));
        }

        return Path.Combine(Folder, serverId + ".json");
    }
}
