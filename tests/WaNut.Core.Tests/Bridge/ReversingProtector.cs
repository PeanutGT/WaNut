using WaNut.Core.Remote;

namespace WaNut.Core.Tests.Bridge;

/// <summary>Stands in for DPAPI off Windows: "encrypts" by reversing the bytes.</summary>
internal sealed class ReversingProtector : ISecretProtector
{
    public byte[] Protect(byte[] plain) => [.. plain.Reverse()];

    public byte[] Unprotect(byte[] protectedData) => [.. protectedData.Reverse()];
}
