namespace WaNut.Core.Jvm;

/// <summary>
/// Builds the java.exe arguments for a server: heap size, garbage collector flags for the Java version, and the jar.
/// </summary>
public static class JvmArguments
{
    /// <summary>Above this heap size Aikar's flags switch to their large-heap values.</summary>
    private const int LargeHeapMb = 12 * 1024;

    // Console text is UTF-8 both ways, whatever the Windows code page is. The sun.* names cover Java 17 and older.
    private static readonly string[] EncodingFlags =
    [
        "-Dfile.encoding=UTF-8",
        "-Dstdout.encoding=UTF-8",
        "-Dstderr.encoding=UTF-8",
        "-Dsun.stdout.encoding=UTF-8",
        "-Dsun.stderr.encoding=UTF-8",
    ];

    public static IReadOnlyList<string> Build(int javaMajor, int memoryMb, string jarFile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(javaMajor, 8);
        ArgumentOutOfRangeException.ThrowIfLessThan(memoryMb, 512);
        ArgumentException.ThrowIfNullOrWhiteSpace(jarFile);

        var args = new List<string> { $"-Xms{memoryMb}M", $"-Xmx{memoryMb}M" };
        args.AddRange(GarbageCollectorFlags(javaMajor, memoryMb));
        args.AddRange(EncodingFlags);
        args.AddRange(["-jar", jarFile, "nogui"]);
        return args;
    }

    public static IReadOnlyList<string> GarbageCollectorFlags(int javaMajor, int memoryMb) => javaMajor switch
    {
        <= 17 => AikarFlags(memoryMb),
        <= 22 => ["-XX:+UseZGC", "-XX:+ZGenerational"],

        // Generational ZGC is the default from Java 23; the extra flag only prints a warning there.
        _ => ["-XX:+UseZGC"],
    };

    /// <summary>Aikar's G1 flags (https://docs.papermc.io/paper/aikars-flags).</summary>
    private static string[] AikarFlags(int memoryMb)
    {
        var large = memoryMb > LargeHeapMb;
        return
        [
            "-XX:+UseG1GC",
            "-XX:+ParallelRefProcEnabled",
            "-XX:MaxGCPauseMillis=200",
            "-XX:+UnlockExperimentalVMOptions",
            "-XX:+DisableExplicitGC",
            "-XX:+AlwaysPreTouch",
            large ? "-XX:G1NewSizePercent=40" : "-XX:G1NewSizePercent=30",
            large ? "-XX:G1MaxNewSizePercent=50" : "-XX:G1MaxNewSizePercent=40",
            large ? "-XX:G1HeapRegionSize=16M" : "-XX:G1HeapRegionSize=8M",
            large ? "-XX:G1ReservePercent=15" : "-XX:G1ReservePercent=20",
            "-XX:G1HeapWastePercent=5",
            "-XX:G1MixedGCCountTarget=4",
            large ? "-XX:InitiatingHeapOccupancyPercent=20" : "-XX:InitiatingHeapOccupancyPercent=15",
            "-XX:G1MixedGCLiveThresholdPercent=90",
            "-XX:G1RSetUpdatingPauseTimePercent=5",
            "-XX:SurvivorRatio=32",
            "-XX:+PerfDisableSharedMem",
            "-XX:MaxTenuringThreshold=1",
            "-Dusing.aikars.flags=https://mcflags.emc.gs",
            "-Daikars.new.flags=true",
        ];
    }
}
