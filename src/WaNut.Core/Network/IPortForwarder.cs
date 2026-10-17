namespace WaNut.Core.Network;

/// <summary>Asks the router to forward a TCP port to this PC (blueprint 2, "Later phases"), and to stop again.</summary>
public interface IPortForwarder
{
    /// <summary>Adds (or renews) the rule for <paramref name="port"/> for <paramref name="lease"/>.</summary>
    Task<PortForwardResult> OpenAsync(int port, TimeSpan lease, string description, CancellationToken cancellationToken = default);

    /// <summary>Removes the rule WaNut added. Does nothing when there is none or the router is gone.</summary>
    Task CloseAsync(int port, CancellationToken cancellationToken = default);
}
