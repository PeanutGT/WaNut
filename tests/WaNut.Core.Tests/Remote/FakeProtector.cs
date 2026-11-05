using System.Security.Cryptography;
using WaNut.Core.Remote;

namespace WaNut.Core.Tests.Remote;

/// <summary>Reverses the bytes and adds a marker, so tests can see the file isn't plain text without needing Windows.</summary>
internal sealed class FakeProtector : ISecretProtector
{
    private const byte Marker = 0xA5;

    public byte[] Protect(byte[] plain) => [Marker, .. plain.Reverse()];

    public byte[] Unprotect(byte[] protectedData) =>
        protectedData.Length > 0 && protectedData[0] == Marker
            ? protectedData[1..].Reverse().ToArray()
            : throw new CryptographicException("Not ours.");
}
