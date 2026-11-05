using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Utilities;
using Org.BouncyCastle.Security;

namespace WaNut.Core.Remote;

/// <summary>A new ed25519 key: the private half in OpenSSH format, and the line to add to ~/.ssh/authorized_keys.</summary>
public sealed record SshKeyPair(string PrivateKey, string PublicKey)
{
    public static SshKeyPair Generate(string comment)
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();

        var privateBlob = OpenSshPrivateKeyUtilities.EncodePrivateKey(pair.Private);
        var pem = new StringBuilder("-----BEGIN OPENSSH PRIVATE KEY-----\n");
        var base64 = Convert.ToBase64String(privateBlob);
        for (var i = 0; i < base64.Length; i += 70)
        {
            pem.Append(base64, i, Math.Min(70, base64.Length - i)).Append('\n');
        }

        pem.Append("-----END OPENSSH PRIVATE KEY-----\n");

        var publicBlob = OpenSshPublicKeyUtilities.EncodePublicKey((Ed25519PublicKeyParameters)pair.Public);
        var cleanComment = new string(comment.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray());
        var publicLine = $"ssh-ed25519 {Convert.ToBase64String(publicBlob)}" + (cleanComment.Length > 0 ? $" {cleanComment}" : string.Empty);
        return new SshKeyPair(pem.ToString(), publicLine);
    }

    /// <summary>
    /// A shell command the user pastes on the VPS (signed in some other way, such as the host's web console) to let
    /// this key in. It only appends, so keys already there keep working.
    /// </summary>
    public static string AuthorizeCommand(string publicKey)
    {
        if (publicKey.Contains('\'') || publicKey.Contains('\n'))
        {
            throw new ArgumentException("Not a public key line.", nameof(publicKey));
        }

        return $"mkdir -p ~/.ssh && chmod 700 ~/.ssh && echo '{publicKey}' >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys";
    }
}
