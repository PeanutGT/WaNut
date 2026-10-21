using System.Runtime.InteropServices;

namespace WaNut.Core.Hosting;

/// <summary>
/// How much RAM a server may use, so it never starves Windows or the Minecraft client running next to it.
/// </summary>
public static class MemoryLimits
{
    public const int MinimumMb = 1024;
    public const int StepMb = 512;
    public const int PreferredDefaultMb = 4096;

    /// <summary>75% of physical memory, rounded down to the slider step, never below the minimum.</summary>
    public static int MaxServerMb(long totalPhysicalMb)
    {
        var max = (int)(totalPhysicalMb * 3 / 4 / StepMb * StepMb);
        return Math.Max(MinimumMb, max);
    }

    public static int DefaultServerMb(long totalPhysicalMb) =>
        Math.Clamp(PreferredDefaultMb, MinimumMb, MaxServerMb(totalPhysicalMb));

    /// <summary>What Windows and the Minecraft client keep when several servers run at once.</summary>
    public const int KeptForWindowsMb = 3072;

    /// <summary>
    /// True when starting a server with <paramref name="startingMb"/> next to the running ones would leave Windows less
    /// than <see cref="KeptForWindowsMb"/>. One server alone is already held to <see cref="MaxServerMb"/> by the slider.
    /// </summary>
    public static bool IsTooMuchTogether(int startingMb, IReadOnlyCollection<int> runningMb, long totalPhysicalMb) =>
        runningMb.Count > 0 && startingMb + runningMb.Sum() > totalPhysicalMb - KeptForWindowsMb;

    public static long TotalPhysicalMb()
    {
        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                return (long)(status.TotalPhys / (1024 * 1024));
            }
        }

        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
