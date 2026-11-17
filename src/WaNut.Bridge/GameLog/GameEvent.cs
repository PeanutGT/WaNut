namespace WaNut.Bridge.GameLog;

/// <summary>Something that happened in the game, read from one log line.</summary>
/// <param name="Player">The player it is about; null for server start and stop.</param>
/// <param name="Text">The chat text, or the whole death or advancement message as the server wrote it.</param>
public sealed record GameEvent(GameEventKind Kind, string? Player = null, string? Text = null);
