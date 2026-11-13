using System.Text;
using WaNut.Core.Remote;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Remote;

public sealed class SecretStoreTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly SecretStore _store;

    public SecretStoreTests() => _store = new SecretStore(new LauncherPaths(_dir.Path), new FakeProtector());

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void SaveThenLoad_RoundTripsAndFileIsNotPlainText()
    {
        _store.Save("abc123", SecretKind.Password, "hunter2-密碼");

        Assert.Equal("hunter2-密碼", _store.Load("abc123", SecretKind.Password));
        var file = Directory.GetFiles(_store.Folder).Single();
        Assert.DoesNotContain("hunter2", Encoding.UTF8.GetString(File.ReadAllBytes(file)), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_NothingSaved_ReturnsNull()
    {
        Assert.Null(_store.Load("abc123", SecretKind.GeneratedKey));
        Assert.False(_store.Has("abc123", SecretKind.GeneratedKey));
    }

    [Fact]
    public void Load_UnreadableFile_ReturnsNull()
    {
        _store.Save("abc123", SecretKind.Password, "x");
        File.WriteAllBytes(Directory.GetFiles(_store.Folder).Single(), [1, 2, 3]);

        Assert.Null(_store.Load("abc123", SecretKind.Password));
    }

    [Fact]
    public void Delete_BeforeAnySecretWasSaved_DoesNothing()
    {
        // The first VPS saved with a key file deletes its (never written) password while the folder is still missing.
        Assert.False(Directory.Exists(_store.Folder));

        _store.Delete("abc123", SecretKind.Password);
        _store.DeleteAll("abc123");

        Assert.False(_store.Has("abc123", SecretKind.Password));
    }

    [Fact]
    public void DeleteAll_RemovesEveryKindForThatVpsOnly()
    {
        _store.Save("abc123", SecretKind.Password, "a");
        _store.Save("abc123", SecretKind.GeneratedKey, "b");
        _store.Save("other1", SecretKind.Password, "c");

        _store.DeleteAll("abc123");

        Assert.False(_store.Has("abc123", SecretKind.Password));
        Assert.False(_store.Has("abc123", SecretKind.GeneratedKey));
        Assert.Equal("c", _store.Load("other1", SecretKind.Password));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("")]
    [InlineData("a b")]
    public void Save_IdThatIsNotLettersAndDigits_Throws(string id)
    {
        Assert.Throws<ArgumentException>(() => _store.Save(id, SecretKind.Password, "x"));
    }

    [Fact]
    public void DpapiProtector_OnWindows_RoundTrips()
    {
        // DPAPI exists only on Windows; CI runs there.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var protector = new DpapiProtector();
        var sealedBytes = protector.Protect("secret"u8.ToArray());

        Assert.NotEqual("secret"u8.ToArray(), sealedBytes);
        Assert.Equal("secret"u8.ToArray(), protector.Unprotect(sealedBytes));
    }
}
