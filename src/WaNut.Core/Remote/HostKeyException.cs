namespace WaNut.Core.Remote;

/// <summary>
/// The VPS showed a host key the user hasn't confirmed: either the first connection (<see cref="Changed"/> false,
/// ask the user to compare the fingerprint) or a key different from the confirmed one (refuse).
/// </summary>
public sealed class HostKeyException(string fingerprint, bool changed)
    : Exception(changed ? "The VPS's host key changed." : "The VPS's host key is not confirmed yet.")
{
    /// <summary>"SHA256:..." as OpenSSH prints it, so the user can compare it with their host's console.</summary>
    public string Fingerprint { get; } = fingerprint;

    public bool Changed { get; } = changed;
}
