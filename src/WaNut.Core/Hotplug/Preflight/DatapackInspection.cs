namespace WaNut.Core.Hotplug.Preflight;

/// <summary>What the pre-flight check found in one data pack zip on this PC.</summary>
/// <param name="FileName">The name it gets in the world's datapacks folder.</param>
/// <param name="Sha256">Lower-case hex, compared with the VPS's copy after the upload.</param>
/// <param name="Metadata">Null when <see cref="Problem"/> says why the zip is no data pack.</param>
/// <param name="RiskyFolders">The data/&lt;namespace&gt;/&lt;folder&gt; names behind <see cref="Risk"/>, such as "dimension".</param>
/// <param name="Problem">Why the pack can't be hot-plugged at all; null when it can.</param>
public sealed record DatapackInspection(
    string LocalPath,
    string FileName,
    long SizeBytes,
    string Sha256,
    PackMetadata? Metadata,
    PackRisk Risk,
    IReadOnlyList<string> RiskyFolders,
    FormatCheck Format,
    int? ExpectedFormat,
    string? Problem)
{
    /// <summary>Hot-plugging is refused: not a data pack, or it adds dimensions.</summary>
    public bool IsBlocked => Problem is not null || Risk == PackRisk.NeedsRestart;

    /// <summary>The user should agree before it goes in: another version's format, or parts that only apply after a restart.</summary>
    public bool NeedsConfirmation => !IsBlocked && (Format == FormatCheck.Mismatch || Risk == PackRisk.AppliesAfterRestart);
}
