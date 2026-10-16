using System.Net;
using WaNut.Core.Players;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Worlds;

public sealed class PlayerIdsTests
{
    [Fact]
    public void OfflineUuid_Notch_MatchesJavaNameUuid()
    {
        // The UUID an offline-mode server gives "Notch", as computed by Java's UUID.nameUUIDFromBytes.
        Assert.Equal("b50ad385-829d-3141-a216-7e7d7539ba7f", PlayerIds.OfflineUuid("Notch").ToString("D"));
    }

    [Fact]
    public async Task FindUuidAsync_KnownName_ReturnsUuid()
    {
        var handler = new StubHttpHandler().Add(MojangProfiles.ProfileUrl("Peanut"), """{"id":"069a79f444e94726a5befca90e38aaf5","name":"Peanut"}""");

        var uuid = await new MojangProfiles(handler.CreateClient()).FindUuidAsync("Peanut");

        Assert.Equal("069a79f4-44e9-4726-a5be-fca90e38aaf5", uuid?.ToString("D"));
    }

    [Fact]
    public async Task FindUuidAsync_UnknownName_ReturnsNull()
    {
        Assert.Null(await new MojangProfiles(new StubHttpHandler().CreateClient()).FindUuidAsync("Nobody"));
    }

    [Fact]
    public async Task FindUuidAsync_ServerError_Throws()
    {
        var client = new HttpClient(new StatusHandler(HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<HttpRequestException>(() => new MojangProfiles(client).FindUuidAsync("Peanut"));
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}
