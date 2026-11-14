using System.Net;
using System.Net.Sockets;
using WaNut.Core.Remote.Reach;

namespace WaNut.Core.Tests.Remote.Reach;

public sealed class VpsReachabilityTests
{
    [Fact]
    public async Task CheckAsync_SomethingListens_Open()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            Assert.Equal(VpsReach.Open, await VpsReachability.CheckAsync("127.0.0.1", port));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task CheckAsync_NothingListens_Refused()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Assert.Equal(VpsReach.Refused, await VpsReachability.CheckAsync("127.0.0.1", port));
    }

    [Fact]
    public async Task CheckAsync_TimeoutPasses_NoAnswer() =>
        Assert.Equal(VpsReach.NoAnswer, await VpsReachability.CheckAsync("192.0.2.1", 25565, TimeSpan.Zero));

    [Fact]
    public async Task CheckAsync_CallerCancels_Throws()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VpsReachability.CheckAsync("192.0.2.1", 25565, cancellationToken: cancel.Token));
    }

    [Theory]
    [InlineData(SocketError.ConnectionRefused, VpsReach.Refused)]
    [InlineData(SocketError.HostUnreachable, VpsReach.Refused)]
    [InlineData(SocketError.TimedOut, VpsReach.NoAnswer)]
    [InlineData(SocketError.HostNotFound, VpsReach.Unknown)]
    [InlineData(SocketError.NetworkUnreachable, VpsReach.Unknown)]
    public void Classify_SocketError_MeansThis(SocketError error, VpsReach expected) =>
        Assert.Equal(expected, VpsReachability.Classify(error));
}
