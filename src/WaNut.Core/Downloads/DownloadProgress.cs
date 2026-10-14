namespace WaNut.Core.Downloads;

public readonly record struct DownloadProgress(long BytesReceived, long? TotalBytes)
{
    /// <summary>0 to 1, or null when the server did not say how big the file is.</summary>
    public double? Fraction => TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value : null;
}
