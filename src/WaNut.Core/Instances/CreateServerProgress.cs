namespace WaNut.Core.Instances;

public enum CreateServerStage
{
    ReadingVersion,
    DownloadingJava,
    DownloadingServer,
    WritingFiles,
}

/// <param name="Fraction">0 to 1 while a download's size is known; otherwise null.</param>
public readonly record struct CreateServerProgress(CreateServerStage Stage, double? Fraction = null);
