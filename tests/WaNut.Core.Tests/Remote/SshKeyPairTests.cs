using System.Text;
using Renci.SshNet;
using WaNut.Core.Remote;

namespace WaNut.Core.Tests.Remote;

public sealed class SshKeyPairTests
{
    [Fact]
    public void Generate_MakesAnEd25519KeySshNetCanRead()
    {
        var pair = SshKeyPair.Generate("WaNut my vps");

        Assert.StartsWith("-----BEGIN OPENSSH PRIVATE KEY-----\n", pair.PrivateKey, StringComparison.Ordinal);
        var parts = pair.PublicKey.Split(' ');
        Assert.Equal("ssh-ed25519", parts[0]);
        Assert.Equal("WaNutmyvps", parts[2]);

        using var key = new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(pair.PrivateKey)));
        Assert.Equal("ssh-ed25519", key.Key.ToString());
    }

    [Fact]
    public void Generate_TwiceGivesDifferentKeys()
    {
        Assert.NotEqual(SshKeyPair.Generate("a").PublicKey, SshKeyPair.Generate("a").PublicKey);
    }

    [Fact]
    public void AuthorizeCommand_AppendsTheKeyWithSafePermissions()
    {
        var command = SshKeyPair.AuthorizeCommand("ssh-ed25519 AAAA wanut");

        Assert.Contains("echo 'ssh-ed25519 AAAA wanut' >> ~/.ssh/authorized_keys", command, StringComparison.Ordinal);
        Assert.Contains("chmod 600 ~/.ssh/authorized_keys", command, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorizeCommand_QuoteInKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => SshKeyPair.AuthorizeCommand("ssh-ed25519 AAAA'; rm -rf ~ #"));
    }
}
