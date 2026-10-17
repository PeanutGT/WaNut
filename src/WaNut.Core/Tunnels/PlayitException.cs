namespace WaNut.Core.Tunnels;

/// <summary>playit.gg refused a request or answered with something WaNut does not understand.</summary>
/// <param name="code">playit's own name for the problem, such as "InvalidAgentKey" or "RequiresVerifiedAccount".</param>
public sealed class PlayitException(string code) : Exception($"playit.gg: {code}")
{
    public string Code { get; } = code;
}
