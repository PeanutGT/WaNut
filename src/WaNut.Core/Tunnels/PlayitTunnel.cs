namespace WaNut.Core.Tunnels;

/// <param name="LocalPort">The port on this PC the tunnel leads to, when it leads to this agent.</param>
/// <param name="Address">What friends type in Minecraft, such as "abc.gl.joinmc.link". Null while playit is still assigning it.</param>
/// <param name="OfflineReasons">playit's reasons the tunnel does not work yet, such as "AgentOverLimit". Empty when it works.</param>
public sealed record PlayitTunnel(Guid Id, string? Name, int? LocalPort, string? Address, IReadOnlyList<string> OfflineReasons);
