using System.Globalization;
using System.Security.Cryptography;
using WaNut.Core.Properties;

namespace WaNut.Core.Bridge;

/// <summary>
/// Makes server.properties ready for the bridge's RCON connection, touching only the keys it needs so comments,
/// order and unknown lines stay as they were.
/// </summary>
public static class RconSetup
{
    public const int DefaultPort = 25575;

    public const int PasswordLength = 24;

    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    /// <summary>
    /// Turns RCON on, binds it to 127.0.0.1 and gives it a password when it has none. An existing password and a valid
    /// port are kept. <c>rcon.ip</c> is not a vanilla key (vanilla binds RCON to <c>server-ip</c>); it is written
    /// for forks that honor it, so the firewall must still keep the port closed.
    /// </summary>
    /// <param name="preferredPort">Used when the file has no valid <c>rcon.port</c>, so servers on one PC do not share a port.</param>
    public static RconSetupResult Apply(PropertiesFile file, int? preferredPort = null)
    {
        var changed = false;

        if (!string.Equals(file.Get("enable-rcon")?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
        {
            file.Set("enable-rcon", "true");
            changed = true;
        }

        if (file.Get("rcon.ip")?.Trim() != "127.0.0.1")
        {
            file.Set("rcon.ip", "127.0.0.1");
            changed = true;
        }

        var port = ReadPort(file.Get("rcon.port"));
        if (port is null)
        {
            port = preferredPort is >= 1 and <= 65535 ? preferredPort.Value : DefaultPort;
            file.Set("rcon.port", port.Value.ToString(CultureInfo.InvariantCulture));
            changed = true;
        }

        var password = file.Get("rcon.password");
        var generated = string.IsNullOrWhiteSpace(password);
        if (generated)
        {
            password = RandomNumberGenerator.GetString(PasswordAlphabet, PasswordLength);
            file.Set("rcon.password", password);
            changed = true;
        }

        return new RconSetupResult(port.Value, password!, changed, generated);
    }

    private static int? ReadPort(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535
            ? port
            : null;
}
