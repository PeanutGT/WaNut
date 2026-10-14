namespace WaNut.Core.Downloads;

public sealed class DownloadVerificationException(Uri url, ExpectedHash expected)
    : Exception($"The file downloaded from {url} does not match its expected {expected.Kind} checksum.")
{
    public Uri Url { get; } = url;

    public ExpectedHash Expected { get; } = expected;
}
