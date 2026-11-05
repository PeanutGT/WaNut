namespace WaNut.Core.Remote;

/// <summary>Encrypts secrets so only this Windows account can read them back.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plain);

    /// <exception cref="System.Security.Cryptography.CryptographicException">The data was made by another account or is damaged.</exception>
    byte[] Unprotect(byte[] protectedData);
}
