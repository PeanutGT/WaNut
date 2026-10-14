using System.Security.Cryptography;

namespace WaNut.Core.Tests.TestSupport;

internal static class Hashes
{
    public static string Sha1(byte[] data) => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
