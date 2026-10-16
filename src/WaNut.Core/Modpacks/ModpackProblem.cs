namespace WaNut.Core.Modpacks;

public enum ModpackProblem
{
    /// <summary>Not a zip, or no readable modrinth.index.json for Minecraft inside.</summary>
    NotAModpack,

    /// <summary>The pack needs Forge, NeoForge or Quilt, which WaNut cannot run yet.</summary>
    UnsupportedLoader,

    /// <summary>A file would land outside the server folder, or comes from a host Modrinth does not allow.</summary>
    UnsafeFile,
}
