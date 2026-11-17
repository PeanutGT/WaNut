using WaNut.Core.Bridge;

namespace WaNut.Core.Tests.Bridge;

public sealed class BridgeSecretsTests
{
    private static readonly BridgeConfig Plain = new()
    {
        ServerId = "s",
        RconPassword = "pw",
        BotToken = "token",
        WebhookUrl = "https://discord.com/api/webhooks/1/x",
        ChannelId = "123",
    };

    [Fact]
    public void Protect_SecretFields_AreEncryptedAndOthersKept()
    {
        var protectedConfig = BridgeSecrets.Protect(Plain, new ReversingProtector());

        Assert.StartsWith(BridgeSecrets.Prefix, protectedConfig.RconPassword, StringComparison.Ordinal);
        Assert.StartsWith(BridgeSecrets.Prefix, protectedConfig.BotToken, StringComparison.Ordinal);
        Assert.StartsWith(BridgeSecrets.Prefix, protectedConfig.WebhookUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("token", BridgeConfigJson.Serialize(protectedConfig), StringComparison.Ordinal);
        Assert.Equal("123", protectedConfig.ChannelId);
        Assert.True(BridgeSecrets.IsProtected(protectedConfig));
    }

    [Fact]
    public void Unprotect_AfterProtect_GivesTheSameConfig()
    {
        var protector = new ReversingProtector();

        var back = BridgeSecrets.Unprotect(BridgeSecrets.Protect(Plain, protector), protector);

        Assert.Equal(Plain with { Filters = back.Filters }, back);
        Assert.False(BridgeSecrets.IsProtected(back));
    }

    [Fact]
    public void Protect_EmptyAndAlreadyProtected_StayAsTheyAre()
    {
        var protector = new ReversingProtector();
        var once = BridgeSecrets.Protect(Plain with { BotToken = null }, protector);

        var twice = BridgeSecrets.Protect(once, protector);

        Assert.Null(twice.BotToken);
        Assert.Equal(once.RconPassword, twice.RconPassword);
    }

    [Fact]
    public void Unprotect_DamagedBase64_ThrowsCryptographicException()
    {
        var damaged = Plain with { RconPassword = BridgeSecrets.Prefix + "%%%" };

        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => BridgeSecrets.Unprotect(damaged, new ReversingProtector()));
    }
}
