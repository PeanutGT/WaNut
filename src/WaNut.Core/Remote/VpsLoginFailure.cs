namespace WaNut.Core.Remote;

public enum VpsLoginFailure
{
    /// <summary>The VPS refused the key or password.</summary>
    Rejected,

    /// <summary>No password or generated key is saved for this VPS (or it was saved on another Windows account).</summary>
    SecretMissing,

    /// <summary>The key file is missing, not a private key, or its passphrase is wrong.</summary>
    BadKeyFile,
}
