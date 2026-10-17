using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Mono.Nat;

namespace WaNut.Core.Network;

/// <summary>
/// Forwards the server's TCP port on the home router with UPnP or NAT-PMP (Mono.Nat). The router found first is
/// remembered; a router that stops answering is searched for again on the next try.
/// </summary>
public sealed class RouterPortForwarder : IPortForwarder
{
    /// <summary>How long to wait for a router to answer the search.</summary>
    public static readonly TimeSpan SearchTime = TimeSpan.FromSeconds(5);

    // UPnP error 725: the router only accepts rules without an end time.
    private const int OnlyPermanentLeasesSupported = 725;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private INatDevice? _router;

    public async Task<PortForwardResult> OpenAsync(int port, TimeSpan lease, string description, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // A remembered router may have gone (another Wi-Fi, a restart): search again once before giving up.
            var remembered = _router is not null;
            var result = await TryOpenAsync(port, lease, description, cancellationToken);
            if (result is null && remembered)
            {
                result = await TryOpenAsync(port, lease, description, cancellationToken);
            }

            return result ?? new PortForwardResult(PortForwardOutcome.NoRouter);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task CloseAsync(int port, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_router is { } router)
            {
                await router.DeletePortMapAsync(new Mapping(Protocol.Tcp, port, port));
            }
        }
        catch (Exception e) when (IsRouterProblem(e))
        {
            // The rule ends with its lease anyway.
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Null when no router answered, or the remembered one stopped answering.</summary>
    private async Task<PortForwardResult?> TryOpenAsync(int port, TimeSpan lease, string description, CancellationToken cancellationToken)
    {
        _router ??= await FindRouterAsync(cancellationToken);
        if (_router is not { } router)
        {
            return null;
        }

        try
        {
            var address = await GetPublicAddressAsync(router);
            if (address is not null && PublicAddressCheck.FindProblem(address) is { } problem)
            {
                return new PortForwardResult(problem, address);
            }

            try
            {
                await router.CreatePortMapAsync(new Mapping(Protocol.Tcp, port, port, (int)lease.TotalSeconds, description));
            }
            catch (MappingException e) when ((int)e.ErrorCode == OnlyPermanentLeasesSupported)
            {
                await router.CreatePortMapAsync(new Mapping(Protocol.Tcp, port, port, 0, description));
            }

            return new PortForwardResult(PortForwardOutcome.Opened, address);
        }
        catch (MappingException e)
        {
            var outcome = e.ErrorCode == ErrorCode.ConflictInMappingEntry ? PortForwardOutcome.Conflict : PortForwardOutcome.Refused;
            return new PortForwardResult(outcome);
        }
        catch (Exception e) when (IsRouterProblem(e))
        {
            _router = null;
            return null;
        }
    }

    private static async Task<IPAddress?> GetPublicAddressAsync(INatDevice router)
    {
        try
        {
            var address = await router.GetExternalIPAsync();
            return address is null || address.Equals(IPAddress.Any) || address.Equals(IPAddress.None) ? null : address;
        }
        catch (MappingException)
        {
            // Some routers don't say; the port can still be forwarded.
            return null;
        }
    }

    private static async Task<INatDevice?> FindRouterAsync(CancellationToken cancellationToken)
    {
        var found = new TaskCompletionSource<INatDevice>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFound(object? sender, DeviceEventArgs e) => found.TrySetResult(e.Device);

        NatUtility.DeviceFound += OnFound;
        try
        {
            NatUtility.StartDiscovery(NatProtocol.Upnp, NatProtocol.Pmp);
            var first = await Task.WhenAny(found.Task, Task.Delay(SearchTime, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            return first == found.Task ? found.Task.Result : null;
        }
        finally
        {
            NatUtility.StopDiscovery();
            NatUtility.DeviceFound -= OnFound;
        }
    }

    private static bool IsRouterProblem(Exception e) =>
        e is MappingException or SocketException or HttpRequestException or IOException or TimeoutException or WebException
        || (e is TaskCanceledException && e.InnerException is TimeoutException);
}
