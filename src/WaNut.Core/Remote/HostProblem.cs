namespace WaNut.Core.Remote;

/// <summary>Something found by <see cref="HostProbe"/> that stops a Minecraft server from running well (or at all).</summary>
public enum HostProblem
{
    /// <summary>Not Linux: WaNut's scripts need Linux.</summary>
    NotLinux,

    /// <summary>Neither x64 nor ARM64, so there is no Java build to install.</summary>
    UnsupportedCpu,

    /// <summary>Less memory than a small server needs.</summary>
    LowMemory,

    /// <summary>Too little free disk space for Java, a server and a world.</summary>
    LowDisk,

    /// <summary>No curl or wget, so the VPS can't download Java.</summary>
    NoDownloader,

    /// <summary>No tar, so the Java download can't be unpacked.</summary>
    NoTar,
}
