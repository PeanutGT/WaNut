namespace WaNut.Core.Remote;

/// <summary>"Test connection": signs in to a VPS and asks what it is.</summary>
public sealed class VpsTester(VpsConnector connector)
{
    /// <exception cref="HostKeyException">The host key isn't the one the user confirmed.</exception>
    /// <exception cref="VpsLoginException">Signing in failed.</exception>
    /// <exception cref="VpsUnreachableException">The VPS couldn't be reached.</exception>
    public async Task<HostInfo> TestAsync(VpsProfile profile, Func<SecretKind, string?>? typedSecret = null, CancellationToken cancellationToken = default)
    {
        using var client = await connector.ConnectAsync(profile, typedSecret, cancellationToken).ConfigureAwait(false);
        try
        {
            var output = await VpsConnector.RunAsync(client, HostProbe.Command, cancellationToken).ConfigureAwait(false);
            return HostProbe.Parse(output);
        }
        finally
        {
            client.Disconnect();
        }
    }
}
