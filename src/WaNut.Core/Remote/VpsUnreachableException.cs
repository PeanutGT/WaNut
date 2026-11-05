namespace WaNut.Core.Remote;

/// <summary>Nothing answered at the VPS's address and port, or the connection broke before signing in.</summary>
public sealed class VpsUnreachableException(Exception inner)
    : Exception($"Couldn't reach the VPS: {inner.Message}", inner);
