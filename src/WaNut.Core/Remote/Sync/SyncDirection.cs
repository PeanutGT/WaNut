namespace WaNut.Core.Remote.Sync;

public enum SyncDirection
{
    /// <summary>This PC to the VPS, before the server starts there.</summary>
    Upload,

    /// <summary>The VPS back to this PC, after the server stops there.</summary>
    Download,
}
