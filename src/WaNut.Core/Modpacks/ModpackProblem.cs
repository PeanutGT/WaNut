namespace WaNut.Core.Modpacks;

public enum ModpackProblem
{
    /// <summary>Not a zip, or neither a readable modrinth.index.json nor a server pack inside.</summary>
    NotAModpack,

    /// <summary>A CurseForge pack for the game (a manifest that lists project ids) instead of its server pack.</summary>
    ClientPack,

    /// <summary>A server pack that doesn't say which Minecraft version and mod loader it needs.</summary>
    UnknownServerPack,

    /// <summary>The pack needs Forge, NeoForge or Quilt, which WaNut cannot run yet.</summary>
    UnsupportedLoader,

    /// <summary>A file would land outside the server folder, or comes from a host Modrinth does not allow.</summary>
    UnsafeFile,
}
