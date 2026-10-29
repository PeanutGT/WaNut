namespace WaNut.Core.Jvm;

/// <summary>
/// Builds the java.exe arguments for a server: heap size, garbage collector flags for the Java version, and the jar.
/// </summary>
public static class JvmArguments
{
    /// <summary>Above this heap size Aikar's flags switch to their large-heap values.</summary>
    private const int LargeHeapMb = 12 * 1024;

    /// <summary>
    /// Up to this heap size G1 is used even where ZGC is available: ZGC keeps extra memory and threads for itself,
    /// which a small server cannot spare.
    /// </summary>
    public const int SmallHeapMb = 4 * 1024;

    // Console text is UTF-8 both ways, whatever the Windows code page is. The sun.* names cover Java 17 and older.
    private static readonly string[] EncodingFlags =
    [
        "-Dfile.encoding=UTF-8",
        "-Dstdout.encoding=UTF-8",
        "-Dstderr.encoding=UTF-8",
        "-Dsun.stdout.encoding=UTF-8",
        "-Dsun.stderr.encoding=UTF-8",
    ];

    /// <param name="jarFile">The jar to run, or "@" and an argument file (Forge and NeoForge from 1.17), which names the classpath and main class itself.</param>
    /// <param name="customArguments">Advanced mode's extra JVM options, added after WaNut's own so they can override them.</param>
    public static IReadOnlyList<string> Build(int javaMajor, int memoryMb, string jarFile, IReadOnlyList<string>? customArguments = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(javaMajor, 8);
        ArgumentOutOfRangeException.ThrowIfLessThan(memoryMb, 512);
        ArgumentException.ThrowIfNullOrWhiteSpace(jarFile);

        var args = new List<string> { $"-Xms{memoryMb}M", $"-Xmx{memoryMb}M" };
        args.AddRange(GarbageCollectorFlags(javaMajor, memoryMb));
        args.AddRange(EncodingFlags);
        args.AddRange(customArguments ?? []);
        args.AddRange(jarFile.StartsWith('@') ? [jarFile, "nogui"] : ["-jar", jarFile, "nogui"]);
        return args;
    }

    /// <summary>
    /// Splits custom JVM options the way a command line would: spaces separate them, double quotes keep spaces
    /// inside one. Returns null when an option is not allowed, so a typo cannot stop the server from starting.
    /// </summary>
    public static IReadOnlyList<string>? ParseCustom(string? text, out CustomArgumentProblem problem)
    {
        problem = CustomArgumentProblem.None;
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (inQuotes)
        {
            problem = CustomArgumentProblem.UnclosedQuote;
            return null;
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        foreach (var arg in result)
        {
            problem = Check(arg);
            if (problem != CustomArgumentProblem.None)
            {
                return null;
            }
        }

        return result;
    }

    private static CustomArgumentProblem Check(string arg)
    {
        if (!arg.StartsWith('-') || arg.Length < 2)
        {
            return CustomArgumentProblem.NotAnOption;
        }

        // The RAM slider owns the heap size, and WaNut picks the jar.
        if (arg.StartsWith("-Xmx", StringComparison.Ordinal) || arg.StartsWith("-Xms", StringComparison.Ordinal))
        {
            return CustomArgumentProblem.Memory;
        }

        return arg is "-jar" or "-cp" or "-classpath" or "--class-path" or "-version" or "--version" or "-help" or "--help" or "-?"
            ? CustomArgumentProblem.NotAllowed
            : CustomArgumentProblem.None;
    }

    public static IReadOnlyList<string> GarbageCollectorFlags(int javaMajor, int memoryMb) => javaMajor switch
    {
        _ when javaMajor <= 17 || memoryMb <= SmallHeapMb => AikarFlags(memoryMb),
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

public enum CustomArgumentProblem
{
    None,

    /// <summary>Every JVM option starts with "-"; anything else would be read as the program to run.</summary>
    NotAnOption,

    /// <summary>-Xmx / -Xms: the RAM slider sets these.</summary>
    Memory,

    /// <summary>-jar, -cp and options that print something and exit.</summary>
    NotAllowed,

    UnclosedQuote,
}
