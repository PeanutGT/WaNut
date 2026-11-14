namespace WaNut.Core.Remote;

/// <summary>What <see cref="HostProbe"/> learned about a VPS.</summary>
public sealed record HostInfo
{
    /// <summary>Below this a Minecraft server can't run usefully (it also needs memory for the system).</summary>
    public const long MinMemoryBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Java (about 200 MB unpacked), a server jar and a young world, with room to grow.</summary>
    public const long MinFreeDiskBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>"Linux" from uname -s.</summary>
    public string Os { get; init; } = string.Empty;

    /// <summary>The raw uname -m, such as x86_64 or aarch64.</summary>
    public string Machine { get; init; } = string.Empty;

    public HostArchitecture Architecture { get; init; }

    /// <summary>The distribution id from /etc/os-release, such as "ubuntu"; empty when unknown.</summary>
    public string DistroId { get; init; } = string.Empty;

    /// <summary>The distribution's own families from /etc/os-release ID_LIKE, such as "debian".</summary>
    public IReadOnlyList<string> DistroLike { get; init; } = [];

    /// <summary>Its full name, such as "Ubuntu 24.04.1 LTS"; empty when unknown.</summary>
    public string DistroName { get; init; } = string.Empty;

    public long MemoryBytes { get; init; }

    public long MemoryMb => MemoryBytes / (1024 * 1024);

    /// <summary>Free space in the home folder, where WaNut keeps everything.</summary>
    public long FreeDiskBytes { get; init; }

    /// <summary>The DMI chassis asset tag, such as "OracleCloud.com"; empty when unreadable.</summary>
    public string AssetTag { get; init; } = string.Empty;

    public string SystemVendor { get; init; } = string.Empty;

    public string ProductName { get; init; } = string.Empty;

    public VpsProvider Provider => HostProbe.ProviderOf(AssetTag, SystemVendor, ProductName);

    public bool HasCurl { get; init; }

    public bool HasWget { get; init; }

    public bool HasTar { get; init; }

    /// <summary>Ubuntu and Debian are what WaNut is tested on; others with the same tools may still work.</summary>
    public bool IsSupportedDistro =>
        DistroId is "ubuntu" or "debian";

    public IReadOnlyList<HostProblem> Problems
    {
        get
        {
            var problems = new List<HostProblem>();
            if (!Os.Equals("Linux", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(HostProblem.NotLinux);
            }

            if (Architecture == HostArchitecture.Other)
            {
                problems.Add(HostProblem.UnsupportedCpu);
            }

            if (MemoryBytes < MinMemoryBytes)
            {
                problems.Add(HostProblem.LowMemory);
            }

            if (FreeDiskBytes < MinFreeDiskBytes)
            {
                problems.Add(HostProblem.LowDisk);
            }

            if (!HasCurl && !HasWget)
            {
                problems.Add(HostProblem.NoDownloader);
            }

            if (!HasTar)
            {
                problems.Add(HostProblem.NoTar);
            }

            return problems;
        }
    }
}
