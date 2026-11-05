using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace WaNut.Core.Remote;

/// <summary>Windows DPAPI for the current user: the files are useless if copied to another account or PC.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiProtector : ISecretProtector
{
    // Keeps WaNut's secrets apart from other apps' DPAPI data for the same user.
    private static readonly byte[] Entropy = "WaNut.Remote.v1"u8.ToArray();

    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedData) => ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
}
