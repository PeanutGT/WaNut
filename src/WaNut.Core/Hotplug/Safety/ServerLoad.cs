namespace WaNut.Core.Hotplug.Safety;

/// <summary>How busy the server is right now.</summary>
/// <param name="Players">Players online, or null when <c>list</c> got no answer.</param>
/// <param name="Mspt">Milliseconds per tick (20 TPS = 50 ms of budget), or null when the server can't tell.</param>
/// <param name="Source">The command that gave <paramref name="Mspt"/>: "mspt", "tick query" or "tps".</param>
public sealed record ServerLoad(int? Players, double? Mspt, string? Source = null);
