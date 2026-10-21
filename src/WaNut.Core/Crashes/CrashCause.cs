namespace WaNut.Core.Crashes;

/// <summary>Why a server stopped or could not start, as far as its output and crash report tell.</summary>
public enum CrashCause
{
    /// <summary>Another program, often a server that is still running, already listens on the port.</summary>
    PortInUse,

    /// <summary>The same mod is in the mods folder twice, usually two versions of it.</summary>
    DuplicateMod,

    /// <summary>A mod needs another mod that is not installed, such as Fabric API.</summary>
    MissingMod,

    /// <summary>A mod is made for another Minecraft version or loader, or does not work with another mod.</summary>
    IncompatibleMod,

    /// <summary>A mod that only works in the game, such as a minimap or shaders, cannot load on a server.</summary>
    ClientOnlyMod,

    /// <summary>The server used up the memory it was given.</summary>
    OutOfMemory,

    /// <summary>Java could not even start with the memory setting, usually because it is more than the PC can give.</summary>
    MemoryTooHigh,

    /// <summary>The server or a mod needs a newer Java.</summary>
    WrongJava,

    /// <summary>The world's files could not be read.</summary>
    BrokenWorld,

    /// <summary>A mod crashed; the output names which one.</summary>
    ModCrashed,
}
