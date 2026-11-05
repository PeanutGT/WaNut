namespace WaNut.Core.Remote;

/// <summary>The kinds of secret a VPS can have in <see cref="SecretStore"/>.</summary>
public enum SecretKind
{
    Password,
    KeyPassphrase,
    GeneratedKey,
}
