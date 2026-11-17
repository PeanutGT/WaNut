using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace WaNut.Core.Remote;

/// <summary>Windows DPAPI for the current user: the files are useless if copied to another account or PC.</summary>
[SupportedOSPlatform("windows")]
/// <param name="entropy">Keeps one use of DPAPI apart from the others for the same user; VPS secrets by default.</param>
public sealed class DpapiProtector(byte[]? entropy = null) : ISecretProtector
{
    // Keeps WaNut's secrets apart from other apps' DPAPI data for the same user.
    private static readonly byte[] RemoteEntropy = "WaNut.Remote.v1"u8.ToArray();

    private readonly byte[] _entropy = entropy ?? RemoteEntropy;

    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, _entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedData) => ProtectedData.Unprotect(protectedData, _entropy, DataProtectionScope.CurrentUser);
}
