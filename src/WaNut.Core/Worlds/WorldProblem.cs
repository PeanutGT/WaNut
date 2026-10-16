namespace WaNut.Core.Worlds;

public enum WorldProblem
{
    /// <summary>The folder or zip has no level.dat, or level.dat can't be read.</summary>
    NotAWorld,

    /// <summary>The zip can't be read, or has paths that would land outside the world folder.</summary>
    DamagedZip,

    /// <summary>The drive doesn't have room for the copy.</summary>
    NotEnoughSpace,

    /// <summary>level-name in server.properties is not a plain folder name, e.g. "../other".</summary>
    UnsafeWorldName,

    /// <summary>The server has no world to export yet.</summary>
    NoWorld,
}
