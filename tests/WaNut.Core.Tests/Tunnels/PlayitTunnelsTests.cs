using System.Text.Json.Nodes;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Tunnels;

namespace WaNut.Core.Tests.Tunnels;

public sealed class PlayitTunnelsTests
{
    private const string Secret = "abc123";
    private static readonly Guid TunnelId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static string Tunnels(int port, string? address)
    {
        var addresses = address is null ? string.Empty : "{\"type\":\"auto\",\"value\":{\"address\":\"" + address + "\",\"source\":{}}}";
        return "{\"status\":\"success\",\"data\":{\"tunnels\":[{\"id\":\"" + TunnelId + "\",\"name\":\"WaNut: Survival\",\"offline_reasons\":[],"
            + "\"origin\":{\"type\":\"agent\",\"details\":{\"config_data\":{\"fields\":["
            + "{\"name\":\"local_ip\",\"value\":\"127.0.0.1\"},{\"name\":\"local_port\",\"value\":\"" + port + "\"}]}}},"
            + "\"connect_addresses\":[" + addresses + "]}]}}";
    }

    private const string NoTunnels = """{"status":"success","data":{"tunnels":[]}}""";

    [Fact]
    public async Task WaitForApproval_AfterVisitAndApproval_ReturnsSecret()
    {
        var stub = new PlayitStubHandler()
            .On("claim/setup", """{"status":"success","data":"WaitingForUserVisit"}""", """{"status":"success","data":"WaitingForUser"}""",
                """{"status":"success","data":"UserAccepted"}""")
            .On("claim/exchange", """{"status":"success","data":{"secret_key":"deadbeef"}}""");
        var statuses = new ListProgress<PlayitClaimStatus>();

        var secret = await new PlayitTunnels(new PlayitApi(stub.CreateClient()))
            .WaitForApprovalAsync("0a1b2c3d4e", "WaNut 1.0", statuses, TimeSpan.Zero);

        Assert.Equal("deadbeef", secret);
        Assert.Equal([PlayitClaimStatus.WaitingForVisit, PlayitClaimStatus.WaitingForApproval, PlayitClaimStatus.Accepted], statuses.Reports);
        Assert.Contains("\"agent_type\":\"self-managed\"", stub.Requests[0].Body);
    }

    [Fact]
    public async Task WaitForApproval_Rejected_Throws()
    {
        var stub = new PlayitStubHandler().On("claim/setup", """{"status":"success","data":"UserRejected"}""");

        var e = await Assert.ThrowsAsync<PlayitException>(() =>
            new PlayitTunnels(new PlayitApi(stub.CreateClient())).WaitForApprovalAsync("0a1b2c3d4e", "WaNut", pollEvery: TimeSpan.Zero));

        Assert.Equal("UserRejected", e.Code);
    }

    [Fact]
    public async Task EnsureTunnel_NoTunnelYet_CreatesOneAndWaitsForItsAddress()
    {
        var stub = new PlayitStubHandler()
            .On("v1/tunnels/list", NoTunnels, Tunnels(25565, null), Tunnels(25565, "abc.gl.joinmc.link"))
            .On("v1/tunnels/create", "{\"status\":\"success\",\"data\":{\"id\":\"" + TunnelId + "\"}}");

        var tunnel = await new PlayitTunnels(new PlayitApi(stub.CreateClient()))
            .EnsureTunnelAsync(Secret, null, "Survival", 25565, TimeSpan.Zero);

        Assert.Equal(TunnelId, tunnel.Id);
        Assert.Equal("abc.gl.joinmc.link", tunnel.Address);
        var create = JsonNode.Parse(stub.Requests.Single(r => r.Path == "v1/tunnels/create").Body)!;
        Assert.Equal("minecraft-java", create["ports"]!["details"]!.GetValue<string>());
        Assert.Equal("25565", create["origin"]!["data"]!["config"]!["fields"]![1]!["value"]!.GetValue<string>());
        Assert.All(stub.Requests, r => Assert.Equal($"Agent-Key {Secret}", r.Authorization));
    }

    [Fact]
    public async Task EnsureTunnel_PortChanged_PointsTheTunnelAtTheNewPort()
    {
        var stub = new PlayitStubHandler()
            .On("v1/tunnels/list", Tunnels(25565, "abc.gl.joinmc.link"))
            .On("v1/tunnels/config", """{"status":"success","data":null}""");

        var tunnel = await new PlayitTunnels(new PlayitApi(stub.CreateClient()))
            .EnsureTunnelAsync(Secret, TunnelId, "Survival", 25570, TimeSpan.Zero);

        Assert.Equal(25570, tunnel.LocalPort);
        Assert.Equal(0, stub.Count("v1/tunnels/create"));
        Assert.Contains("\"25570\"", stub.Requests.Single(r => r.Path == "v1/tunnels/config").Body);
    }

    [Fact]
    public async Task EnsureTunnel_SamePort_ChangesNothing()
    {
        var stub = new PlayitStubHandler().On("v1/tunnels/list", Tunnels(25565, "abc.gl.joinmc.link"));

        var tunnel = await new PlayitTunnels(new PlayitApi(stub.CreateClient()))
            .EnsureTunnelAsync(Secret, TunnelId, "Survival", 25565, TimeSpan.Zero);

        Assert.Equal("abc.gl.joinmc.link", tunnel.Address);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task EnsureTunnel_FreeLimitReached_ThrowsPlayitsReason()
    {
        var stub = new PlayitStubHandler()
            .On("v1/tunnels/list", NoTunnels)
            .On("v1/tunnels/create", """{"status":"fail","data":"RequiresPlayitPremium"}""");

        var e = await Assert.ThrowsAsync<PlayitException>(() =>
            new PlayitTunnels(new PlayitApi(stub.CreateClient())).EnsureTunnelAsync(Secret, null, "Survival", 25565, TimeSpan.Zero));

        Assert.Equal("RequiresPlayitPremium", e.Code);
    }

    [Fact]
    public async Task ListTunnels_BadKey_ThrowsAuthMessage()
    {
        var stub = new PlayitStubHandler().On("v1/tunnels/list", """{"status":"error","data":{"type":"auth","message":"InvalidAgentKey"}}""");

        var e = await Assert.ThrowsAsync<PlayitException>(() => new PlayitApi(stub.CreateClient()).ListTunnelsAsync(Secret));

        Assert.Equal("InvalidAgentKey", e.Code);
    }

    [Theory]
    [InlineData("Survival", "WaNut: Survival")]
    [InlineData("生存服", "WaNut server")]
    [InlineData("我的 Survival 2", "WaNut: Survival 2")]
    [InlineData("A very long server name that goes on", "WaNut: A very long server name")]
    public void TunnelName_KeepsPrintableAsciiWithinLimit(string serverName, string expected)
    {
        Assert.Equal(expected, PlayitTunnels.TunnelName(serverName));
    }

    [Fact]
    public void NewClaimCode_IsTenHexCharacters()
    {
        Assert.Matches("^[0-9a-f]{10}$", PlayitApi.NewClaimCode());
    }
}
