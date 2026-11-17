namespace WaNut.Core.Remote.Sync;

/// <summary>The archive transfer failed on the VPS side (a missing tool, a full disk, a size that didn't match).</summary>
public sealed class BulkTransferException(string message, Exception? inner = null) : Exception(message, inner);
