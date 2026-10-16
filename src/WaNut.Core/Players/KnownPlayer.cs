namespace WaNut.Core.Players;

/// <summary>A player the server has seen, from usercache.json.</summary>
public sealed record KnownPlayer(string Name, Guid Uuid);
