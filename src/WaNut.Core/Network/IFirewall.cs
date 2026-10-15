namespace WaNut.Core.Network;

/// <summary>Reads and adds the Windows Defender Firewall rule that lets friends reach WaNut's java.exe.</summary>
public interface IFirewall
{
    Task<FirewallState> GetStateAsync(string program, CancellationToken cancellationToken = default);

    /// <summary>Asks Windows for administrator approval (one UAC prompt), then adds the allow rule.</summary>
    Task<FirewallChange> AllowAsync(string program, string ruleName, CancellationToken cancellationToken = default);
}
