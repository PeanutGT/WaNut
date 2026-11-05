namespace WaNut.Core.Remote;

/// <summary>How WaNut signs in to a VPS.</summary>
public enum VpsLogin
{
    /// <summary>A key WaNut made; its private half is kept encrypted in WaNut's data folder.</summary>
    GeneratedKey,

    /// <summary>A private key file the user already has, with an optional passphrase.</summary>
    KeyFile,

    /// <summary>The account's password.</summary>
    Password,
}
