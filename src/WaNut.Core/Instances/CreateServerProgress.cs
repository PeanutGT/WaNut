namespace WaNut.Core.Instances;

public enum CreateServerStage
{
    ReadingVersion,
    DownloadingJava,
    DownloadingServer,

    /// <summary>Forge and NeoForge only: their installer sets up the server and downloads its libraries.</summary>
    InstallingLoader,

    /// <summary>Changing type or version only: the world is backed up before any server file is replaced.</summary>
    BackingUp,
    WritingFiles,
}

/// <param name="Fraction">0 to 1 while a download's size is known; otherwise null.</param>
public readonly record struct CreateServerProgress(CreateServerStage Stage, double? Fraction = null);
