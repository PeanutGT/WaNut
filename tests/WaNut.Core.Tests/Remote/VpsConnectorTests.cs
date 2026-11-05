using WaNut.Core.Remote;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Remote;

public sealed class VpsConnectorTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void FormatFingerprint_AddsOpenSshPrefix()
    {
        Assert.Equal("SHA256:abc", VpsConnector.FormatFingerprint("abc"));
    }

    [Fact]
    public void CheckHostKey_FirstConnection_AsksWithFingerprint()
    {
        var e = Assert.Throws<HostKeyException>(() => VpsConnector.CheckHostKey(null, "SHA256:new"));

        Assert.False(e.Changed);
        Assert.Equal("SHA256:new", e.Fingerprint);
    }

    [Fact]
    public void CheckHostKey_DifferentKey_RefusesAsChanged()
    {
        var e = Assert.Throws<HostKeyException>(() => VpsConnector.CheckHostKey("SHA256:old", "SHA256:new"));

        Assert.True(e.Changed);
    }

    [Fact]
    public void CheckHostKey_SameKey_Passes()
    {
        VpsConnector.CheckHostKey("SHA256:same", "SHA256:same");
    }

    [Fact]
    public async Task ConnectAsync_PasswordNotSaved_FailsBeforeConnecting()
    {
        var connector = new VpsConnector(new SecretStore(new LauncherPaths(_dir.Path), new FakeProtector()));
        var profile = new VpsProfile { Name = "x", Host = "192.0.2.1", User = "mc", Login = VpsLogin.Password };

        var e = await Assert.ThrowsAsync<VpsLoginException>(() => connector.ConnectAsync(profile));

        Assert.Equal(VpsLoginFailure.SecretMissing, e.Failure);
    }

    [Fact]
    public async Task ConnectAsync_KeyFileMissing_SaysBadKeyFile()
    {
        var connector = new VpsConnector(new SecretStore(new LauncherPaths(_dir.Path), new FakeProtector()));
        var profile = new VpsProfile { Name = "x", Host = "192.0.2.1", User = "mc", Login = VpsLogin.KeyFile, KeyFile = Path.Combine(_dir.Path, "nope") };

        var e = await Assert.ThrowsAsync<VpsLoginException>(() => connector.ConnectAsync(profile));

        Assert.Equal(VpsLoginFailure.BadKeyFile, e.Failure);
    }
}
