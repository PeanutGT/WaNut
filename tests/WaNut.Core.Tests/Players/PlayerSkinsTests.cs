using System.Text;
using WaNut.Core.Players;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Players;

public sealed class PlayerSkinsTests : IDisposable
{
    private static readonly Guid AlexId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Uri SkinUrl = new("https://textures.minecraft.net/texture/abc123");
    private static readonly byte[] SkinBytes = [0x89, 0x50, 0x4e, 0x47];

    private readonly TempDir _dir = new();
    private readonly StubHttpHandler _http = new();
    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);
    private readonly PlayerSkins _skins;

    public PlayerSkinsTests()
    {
        var client = _http.CreateClient();
        _skins = new PlayerSkins(client, new MojangProfiles(client), new LauncherPaths(_dir.Path), _clock);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void ParseSkinUrl_CustomSkin_ReturnsHttpsAddress()
    {
        Assert.Equal(SkinUrl, PlayerSkins.ParseSkinUrl(Profile("http://textures.minecraft.net/texture/abc123")));
    }

    [Theory]
    [InlineData("""{"id":"x","name":"Alex","properties":[]}""")]
    [InlineData("not json")]
    public void ParseSkinUrl_NoSkin_ReturnsNull(string json)
    {
        Assert.Null(PlayerSkins.ParseSkinUrl(json));
    }

    [Fact]
    public void ParseSkinUrl_OtherHost_ReturnsNull()
    {
        Assert.Null(PlayerSkins.ParseSkinUrl(Profile("https://example.com/skin.png")));
    }

    [Fact]
    public async Task GetSkinFile_Downloads_ThenUsesTheCache()
    {
        ServeAlex();

        var file = await _skins.GetSkinFileAsync("Alex");
        var requests = _http.Requests.Count;
        var again = await _skins.GetSkinFileAsync("alex");

        Assert.NotNull(file);
        Assert.Equal(SkinBytes, File.ReadAllBytes(file));
        Assert.Equal(file, again);
        Assert.Equal(requests, _http.Requests.Count);
    }

    [Fact]
    public async Task GetSkinFile_CacheExpired_DownloadsAgain()
    {
        ServeAlex();
        await _skins.GetSkinFileAsync("Alex");
        var requests = _http.Requests.Count;

        _clock.Advance(PlayerSkins.CacheLife + TimeSpan.FromMinutes(1));
        await _skins.GetSkinFileAsync("Alex");

        Assert.True(_http.Requests.Count > requests);
    }

    [Fact]
    public async Task GetSkinFile_NoAccount_IsNullAndNotAskedAgain()
    {
        Assert.Null(await _skins.GetSkinFileAsync("Nobody"));
        var requests = _http.Requests.Count;

        Assert.Null(await _skins.GetSkinFileAsync("Nobody"));
        Assert.Equal(requests, _http.Requests.Count);
    }

    [Fact]
    public async Task GetSkinFile_InvalidName_AsksNobody()
    {
        Assert.Null(await _skins.GetSkinFileAsync("../evil"));
        Assert.Empty(_http.Requests);
    }

    private void ServeAlex()
    {
        _http.Add(MojangProfiles.ProfileUrl("Alex"), $$"""{"id":"{{AlexId:N}}","name":"Alex"}""")
            .Add(PlayerSkins.SessionProfileUrl(AlexId), Profile("http://textures.minecraft.net/texture/abc123"))
            .Add(SkinUrl, SkinBytes);
    }

    private static string Profile(string skinUrl)
    {
        var textures = Convert.ToBase64String(Encoding.UTF8.GetBytes($$$$"""{"textures":{"SKIN":{"url":"{{{{skinUrl}}}}"}}}"""));
        return $$"""{"id":"x","name":"Alex","properties":[{"name":"textures","value":"{{textures}}"}]}""";
    }
}
