using System.Security.Cryptography;
using System.Text;
using WaNut.Core.Remote;

namespace WaNut.Core.Bridge;

/// <summary>
/// Encrypts the secret fields of a <see cref="BridgeConfig"/> for <c>bridge.config.json</c> on this PC, so a copied
/// file is useless on another Windows account. Each one becomes "dpapi:" and base64; the bridge reverses it when it
/// starts. On a VPS the file stays plain text behind <c>chmod 600</c>.
/// </summary>
public static class BridgeSecrets
{
    public const string Prefix = "dpapi:";

    /// <summary>Keeps the bridge's DPAPI data apart from the VPS secrets of the same user.</summary>
    public static byte[] Entropy => "WaNut.Bridge.v1"u8.ToArray();

    public static BridgeConfig Protect(BridgeConfig config, ISecretProtector protector) => config with
    {
        RconPassword = Protect(config.RconPassword, protector)!,
        BotToken = Protect(config.BotToken, protector),
        WebhookUrl = Protect(config.WebhookUrl, protector),
        Filters = config.Filters with { },
    };

    /// <exception cref="CryptographicException">A field was encrypted by another Windows account or is damaged.</exception>
    public static BridgeConfig Unprotect(BridgeConfig config, ISecretProtector protector) => config with
    {
        RconPassword = Unprotect(config.RconPassword, protector)!,
        BotToken = Unprotect(config.BotToken, protector),
        WebhookUrl = Unprotect(config.WebhookUrl, protector),
        Filters = config.Filters with { },
    };

    /// <summary>True when any secret field is still encrypted.</summary>
    public static bool IsProtected(BridgeConfig config) =>
        IsProtected(config.RconPassword) || IsProtected(config.BotToken) || IsProtected(config.WebhookUrl);

    private static bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    private static string? Protect(string? value, ISecretProtector protector) =>
        string.IsNullOrEmpty(value) || IsProtected(value)
            ? value
            : Prefix + Convert.ToBase64String(protector.Protect(Encoding.UTF8.GetBytes(value)));

    private static string? Unprotect(string? value, ISecretProtector protector)
    {
        if (!IsProtected(value))
        {
            return value;
        }

        try
        {
            return Encoding.UTF8.GetString(protector.Unprotect(Convert.FromBase64String(value![Prefix.Length..])));
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("A bridge secret is not valid base64.", ex);
        }
    }
}
