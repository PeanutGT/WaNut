namespace WaNut.Core.Remote;

/// <summary>
/// One of the user's own Linux hosts, reached over SSH. Stored in config.json without any secret: passwords,
/// passphrases and generated keys live in <see cref="SecretStore"/> under <see cref="Id"/>.
/// </summary>
public sealed record VpsProfile
{
    public const int DefaultPort = 22;

    /// <summary>Names this VPS's secrets and, later, the servers that run on it. Never changes.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = string.Empty;

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = DefaultPort;

    public string User { get; init; } = string.Empty;

    public VpsLogin Login { get; init; }

    /// <summary>The private key file for <see cref="VpsLogin.KeyFile"/>; null otherwise.</summary>
    public string? KeyFile { get; init; }

    /// <summary>
    /// For <see cref="VpsLogin.GeneratedKey"/>: the public half, kept so it can be shown again to add to the VPS. Not
    /// a secret.
    /// </summary>
    public string? PublicKey { get; init; }

    /// <summary>
    /// The host key fingerprint ("SHA256:...") the user confirmed on the first connection. Null until then. A host
    /// that later shows another key is refused, because someone may be in the middle.
    /// </summary>
    public string? HostKey { get; init; }

    /// <summary>
    /// The VPS's RAM in MB, read by "Test connection" and again at each start; 0 until then. It caps the Server tab's
    /// memory slider for servers that run here, since this PC's RAM says nothing about the VPS.
    /// </summary>
    public long MemoryMb { get; init; }

    /// <summary>What friends type in Minecraft to join a server on this VPS: "host:port", with brackets around IPv6.</summary>
    public string Endpoint(int port)
    {
        var host = Host.Trim().Trim('[', ']');
        var isIpv6 = System.Net.IPAddress.TryParse(host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
        var portText = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return isIpv6 ? $"[{host}]:{portText}" : $"{host}:{portText}";
    }

    /// <summary>The first thing that keeps this profile from being used, or null when every field is filled in.</summary>
    public VpsProfileProblem? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return VpsProfileProblem.NoName;
        }

        if (string.IsNullOrWhiteSpace(Host) || Host.Any(char.IsWhiteSpace) || Uri.CheckHostName(Host.Trim('[', ']')) == UriHostNameType.Unknown)
        {
            return VpsProfileProblem.BadHost;
        }

        if (Port is < 1 or > 65535)
        {
            return VpsProfileProblem.BadPort;
        }

        if (string.IsNullOrWhiteSpace(User) || User.Any(char.IsWhiteSpace))
        {
            return VpsProfileProblem.NoUser;
        }

        if (Login == VpsLogin.KeyFile && string.IsNullOrWhiteSpace(KeyFile))
        {
            return VpsProfileProblem.NoKeyFile;
        }

        return null;
    }
}
