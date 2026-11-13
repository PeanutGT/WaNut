using System.Security.Cryptography;
using System.Text;

namespace WaNut.Core.Remote;

/// <summary>
/// Secrets for each VPS (password, key passphrase, generated private key), encrypted one file per secret under
/// %APPDATA%\WaNut\secrets. Nothing here is ever written to a log or shown again after it is typed.
/// </summary>
public sealed class SecretStore(LauncherPaths paths, ISecretProtector protector)
{
    public string Folder { get; } = Path.Combine(paths.Root, "secrets");

    public void Save(string profileId, SecretKind kind, string value)
    {
        Directory.CreateDirectory(Folder);
        var path = PathFor(profileId, kind);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, protector.Protect(Encoding.UTF8.GetBytes(value)));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>The secret, or null when none was saved or it can't be read on this account any more.</summary>
    public string? Load(string profileId, SecretKind kind)
    {
        var path = PathFor(profileId, kind);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(protector.Unprotect(File.ReadAllBytes(path)));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public bool Has(string profileId, SecretKind kind) => File.Exists(PathFor(profileId, kind));

    /// <summary>Does nothing when the secret, or the whole folder, was never written.</summary>
    public void Delete(string profileId, SecretKind kind)
    {
        var path = PathFor(profileId, kind);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Removes every secret of a VPS, when the user removes it from WaNut.</summary>
    public void DeleteAll(string profileId)
    {
        foreach (var kind in Enum.GetValues<SecretKind>())
        {
            Delete(profileId, kind);
        }
    }

    private string PathFor(string profileId, SecretKind kind)
    {
        // Ids are WaNut's own GUIDs; anything else could step outside the folder.
        if (profileId.Length == 0 || !profileId.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("A VPS id is letters and digits only.", nameof(profileId));
        }

        return Path.Combine(Folder, $"{profileId}.{kind.ToString().ToLowerInvariant()}");
    }
}
