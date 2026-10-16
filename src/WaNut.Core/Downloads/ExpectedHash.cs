using System.Security.Cryptography;

namespace WaNut.Core.Downloads;

public enum HashKind
{
    Sha1,
    Sha256,
    Sha512,
}

/// <summary>
/// The checksum a downloaded file must match, as published by its source.
/// </summary>
public sealed record ExpectedHash(HashKind Kind, string Hex)
{
    public static ExpectedHash Sha1(string hex) => new(HashKind.Sha1, hex);

    public static ExpectedHash Sha256(string hex) => new(HashKind.Sha256, hex);

    public static ExpectedHash Sha512(string hex) => new(HashKind.Sha512, hex);

    internal IncrementalHash CreateHasher() => Kind switch
    {
        HashKind.Sha1 => IncrementalHash.CreateHash(HashAlgorithmName.SHA1),
        HashKind.Sha256 => IncrementalHash.CreateHash(HashAlgorithmName.SHA256),
        HashKind.Sha512 => IncrementalHash.CreateHash(HashAlgorithmName.SHA512),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null),
    };

    public bool Matches(ReadOnlySpan<byte> hash) =>
        string.Equals(Convert.ToHexString(hash), Hex, StringComparison.OrdinalIgnoreCase);

    public bool MatchesFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var hasher = CreateHasher();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hasher.AppendData(buffer, 0, read);
        }

        return Matches(hasher.GetHashAndReset());
    }
}
