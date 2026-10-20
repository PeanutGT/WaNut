namespace WaNut.Core.Backups;

/// <summary>A part of a world that can be restored on its own. The Overworld also holds the players' files and anything a mod adds.</summary>
public enum WorldPart
{
    Overworld,
    Nether,
    End,
}
