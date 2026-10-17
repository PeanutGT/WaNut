using System.Net;
using WaNut.Core.Network;

namespace WaNut.Core.Tests.Network;

public sealed class PublicAddressCheckTests
{
    [Theory]
    [InlineData("100.64.0.1")]
    [InlineData("100.100.12.34")]
    [InlineData("100.127.255.254")]
    public void SharedCarrierAddress_IsCgnat(string address) =>
        Assert.Equal(PortForwardOutcome.Cgnat, PublicAddressCheck.FindProblem(IPAddress.Parse(address)));

    [Theory]
    [InlineData("10.0.0.138")]
    [InlineData("172.16.5.1")]
    [InlineData("172.31.255.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.3.4")]
    public void PrivateAddress_MeansASecondRouter(string address) =>
        Assert.Equal(PortForwardOutcome.DoubleNat, PublicAddressCheck.FindProblem(IPAddress.Parse(address)));

    [Theory]
    [InlineData("1.34.56.78")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("2001:db8::1")]
    public void PublicAddress_HasNoProblem(string address) =>
        Assert.Null(PublicAddressCheck.FindProblem(IPAddress.Parse(address)));
}
