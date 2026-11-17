using WaNut.Core.Remote;

namespace WaNut.Core.Bridge;

/// <summary>The three bridge builds, bundled in the app folder as bridge\&lt;rid&gt;\ (Peanut's choice C: no download).</summary>
public static class BridgeBinaries
{
    public const string WinX64 = "win-x64";
    public const string LinuxX64 = "linux-x64";
    public const string LinuxArm64 = "linux-arm64";

    public static IReadOnlyList<string> Rids { get; } = [WinX64, LinuxX64, LinuxArm64];

    /// <summary>The executable's own name, as dotnet publish makes it.</summary>
    public static string FileName(string rid) => rid.StartsWith("win", StringComparison.Ordinal) ? "WaNut.Bridge.exe" : "WaNut.Bridge";

    /// <summary>The Linux build for a VPS CPU; null for one the bridge is not built for.</summary>
    public static string? RidFor(HostArchitecture architecture) => architecture switch
    {
        HostArchitecture.X64 => LinuxX64,
        HostArchitecture.Arm64 => LinuxArm64,
        _ => null,
    };
}
