namespace WaNut.Core.Remote;

/// <summary>The VPS answered but WaNut couldn't sign in, or the saved secret is missing or unreadable.</summary>
public sealed class VpsLoginException(VpsLoginFailure failure, Exception? inner = null)
    : Exception($"Couldn't sign in to the VPS: {failure}.", inner)
{
    public VpsLoginFailure Failure { get; } = failure;
}
