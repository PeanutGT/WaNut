using System.Globalization;
using System.Text.RegularExpressions;
using WaNut.Core.Hosting;

namespace WaNut.Core.Crashes;

/// <summary>
/// Finds out why a server stopped from its console output and crash report: Vanilla and Paper messages, Fabric's
/// "Incompatible mods found!" list, Forge's and NeoForge's mod loading errors, and mixin or entrypoint failures that
/// name a mod. Returns null when nothing it knows shows up.
/// </summary>
public static partial class CrashAnalyzer
{
    /// <summary>Ids loaders use for themselves, the game and Java: never a mod to install or turn off.</summary>
    private static readonly HashSet<string> NotMods = new(StringComparer.OrdinalIgnoreCase)
    {
        "minecraft", "java", "fabricloader", "fabric-loader", "quilt_loader", "forge", "neoforge", "fml", "javafml", "mixin",
    };

    public static CrashDiagnosis? Analyze(IEnumerable<string> output, string? crashReport = null)
    {
        var lines = output
            .Concat(crashReport?.Split('\n') ?? [])
            .Select(CleanLine)
            .Where(line => line.Length > 0 && !IsChat(line))
            .ToList();
        return PortInUse(lines)
            ?? MemoryTooHigh(lines)
            ?? JavaTooOld(lines)
            ?? Duplicates(lines)
            ?? Dependencies(lines)
            ?? ClientOnly(lines)
            ?? OutOfMemory(lines)
            ?? BrokenWorld(lines)
            ?? ModCrashed(lines);
    }

    /// <summary>The newest crash report the server wrote at or after <paramref name="sinceUtc"/>, or null.</summary>
    public static string? FindCrashReport(string instanceDir, DateTime sinceUtc)
    {
        var folder = Path.Combine(instanceDir, "crash-reports");
        if (!Directory.Exists(folder))
        {
            return null;
        }

        // A little slack: file times and the start time come from different clocks' rounding.
        var since = sinceUtc - TimeSpan.FromSeconds(2);
        return new DirectoryInfo(folder).EnumerateFiles("*.txt")
            .Where(f => f.LastWriteTimeUtc >= since)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName)
            .FirstOrDefault();
    }

    private static string CleanLine(string line) => FormattingCode().Replace(ServerLogParser.Clean(line), string.Empty).TrimEnd('\r', ' ');

    // Players can type anything, including a line that looks like an error; their chat never counts.
    private static bool IsChat(string line)
    {
        var start = line.IndexOf("]: ", StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var message = line.AsSpan(start + 3);
        return message.StartsWith("<") || message.StartsWith("[Not Secure]") || message.StartsWith("[Server]");
    }

    private static CrashDiagnosis? PortInUse(List<string> lines)
    {
        if (!lines.Any(l => l.Contains("FAILED TO BIND TO PORT", StringComparison.Ordinal)
            || (l.Contains("BindException", StringComparison.Ordinal) && l.Contains("already in use", StringComparison.OrdinalIgnoreCase))))
        {
            return null;
        }

        var port = lines.Select(l => StartingOn().Match(l)).LastOrDefault(m => m.Success)?.Groups["port"].Value;
        return CrashDiagnosis.Of(CrashCause.PortInUse, port);
    }

    private static CrashDiagnosis? MemoryTooHigh(List<string> lines) =>
        lines.Any(l => l.Contains("Could not reserve enough space for", StringComparison.Ordinal)
            || l.Contains("Invalid maximum heap size", StringComparison.Ordinal)
            || l.Contains("Invalid initial heap size", StringComparison.Ordinal)
            || l.Contains("insufficient memory for the Java Runtime Environment", StringComparison.Ordinal)
            || l.Contains("Initial heap size set to a larger value than the maximum heap size", StringComparison.Ordinal))
            ? CrashDiagnosis.Of(CrashCause.MemoryTooHigh)
            : null;

    private static CrashDiagnosis? JavaTooOld(List<string> lines)
    {
        var line = lines.FirstOrDefault(l => l.Contains("UnsupportedClassVersionError", StringComparison.Ordinal)
            || l.Contains("compiled by a more recent version of the Java Runtime", StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        // Class file version 65 is Java 21: the major version is the class file version minus 44.
        var match = ClassFileVersion().Match(line);
        var java = match.Success ? (int.Parse(match.Groups["v"].Value, CultureInfo.InvariantCulture) - 44).ToString(CultureInfo.InvariantCulture) : null;
        return CrashDiagnosis.Of(CrashCause.WrongJava, java);
    }

    private static CrashDiagnosis? Duplicates(List<string> lines)
    {
        var ids = new List<string>();
        var files = new List<string>();
        foreach (var line in lines)
        {
            if (ForgeDuplicate().Match(line) is { Success: true } forge)
            {
                ids.Add(forge.Groups["id"].Value);
                files.AddRange(SplitFiles(forge.Groups["files"].Value));
            }
            else if (NeoForgeDuplicate().Match(line) is { Success: true } neo)
            {
                ids.Add(neo.Groups["id"].Value);
                files.AddRange(SplitFiles(neo.Groups["files"].Value));
            }
            else if (FabricDuplicate().Match(line) is { Success: true } fabric)
            {
                ids.Add(fabric.Groups["id"].Value);
            }
        }

        return ids.Count == 0
            ? null
            : new CrashDiagnosis(CrashCause.DuplicateMod, Distinct(ids), Distinct(files));
    }

    private static CrashDiagnosis? Dependencies(List<string> lines)
    {
        var missing = new List<string>();
        var needing = new List<string>();
        var wrongVersion = new List<(string Mod, string Dependency, string Wanted)>();
        var clientOnly = new List<string>();
        var conflicts = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (FabricMissing().Match(line) is { Success: true } fabricMissing)
            {
                needing.Add(fabricMissing.Groups["mod"].Value);
                missing.Add(IdIn(fabricMissing.Groups["dep"].Value));
            }
            else if (FabricWrongVersion().Match(line) is { Success: true } fabricWrong)
            {
                wrongVersion.Add((fabricWrong.Groups["mod"].Value, IdIn(fabricWrong.Groups["dep"].Value), fabricWrong.Groups["wanted"].Value));
            }
            else if (FabricClientOnlyDependency().Match(line) is { Success: true } fabricClient)
            {
                clientOnly.Add(fabricClient.Groups["mod"].Value);
            }
            else if (FabricBreaks().Match(line) is { Success: true } fabricBreaks)
            {
                conflicts.Add(fabricBreaks.Groups["mod"].Value);
                conflicts.Add(IdIn(fabricBreaks.Groups["dep"].Value));
            }
            else if (ForgeDependency().Match(line) is { Success: true } forge)
            {
                if (forge.Groups["actual"].Value.Contains("MISSING", StringComparison.OrdinalIgnoreCase))
                {
                    needing.Add(forge.Groups["mod"].Value);
                    missing.Add(forge.Groups["dep"].Value);
                }
                else
                {
                    wrongVersion.Add((forge.Groups["mod"].Value, forge.Groups["dep"].Value, forge.Groups["range"].Value));
                }
            }
            else if (NeoForgeDependency().Match(line) is { Success: true } neo)
            {
                // The next line says "Currently, <dep> is not installed" or names the version that is.
                var notInstalled = lines.Skip(i + 1).Take(2).Any(l => l.Contains("not installed", StringComparison.OrdinalIgnoreCase));
                if (notInstalled)
                {
                    needing.Add(neo.Groups["mod"].Value);
                    missing.Add(neo.Groups["dep"].Value);
                }
                else
                {
                    wrongVersion.Add((neo.Groups["mod"].Value, neo.Groups["dep"].Value, neo.Groups["wanted"].Value));
                }
            }
        }

        // The game or the loader can't be "installed": a mod asking for them is made for another version.
        foreach (var (mod, dep) in needing.Zip(missing).Where(p => NotMods.Contains(p.Second)).ToList())
        {
            wrongVersion.Add((mod, dep, string.Empty));
        }

        missing.RemoveAll(NotMods.Contains);
        if (missing.Count > 0)
        {
            return new CrashDiagnosis(CrashCause.MissingMod, Distinct(missing), [], needing.FirstOrDefault());
        }

        if (wrongVersion.FirstOrDefault(w => w.Dependency.Equals("java", StringComparison.OrdinalIgnoreCase)) is { Mod: not null } java)
        {
            var major = FirstNumber().Match(java.Wanted);
            return new CrashDiagnosis(CrashCause.WrongJava, Distinct([java.Mod]), [], major.Success ? major.Value : null);
        }

        if (wrongVersion.Count > 0)
        {
            return new CrashDiagnosis(CrashCause.IncompatibleMod, Distinct(wrongVersion.Select(w => w.Mod)), [], wrongVersion[0].Dependency);
        }

        if (clientOnly.Count > 0)
        {
            return new CrashDiagnosis(CrashCause.ClientOnlyMod, Distinct(clientOnly), []);
        }

        return conflicts.Count > 0 ? new CrashDiagnosis(CrashCause.IncompatibleMod, Distinct(conflicts), []) : null;
    }

    private static CrashDiagnosis? ClientOnly(List<string> lines) =>
        lines.Any(l => l.Contains("for invalid dist DEDICATED_SERVER", StringComparison.Ordinal)
            || l.Contains("in environment type SERVER", StringComparison.Ordinal)
            || l.Contains("NoClassDefFoundError: net/minecraft/client/", StringComparison.Ordinal)
            || l.Contains("ClassNotFoundException: net.minecraft.client.", StringComparison.Ordinal))
            ? new CrashDiagnosis(CrashCause.ClientOnlyMod, BlamedMods(lines), [])
            : null;

    private static CrashDiagnosis? OutOfMemory(List<string> lines) =>
        lines.Any(l => l.Contains("java.lang.OutOfMemoryError", StringComparison.Ordinal))
            ? CrashDiagnosis.Of(CrashCause.OutOfMemory)
            : null;

    private static CrashDiagnosis? BrokenWorld(List<string> lines) =>
        lines.Any(l => l.Contains("level.dat", StringComparison.Ordinal)
            && (l.Contains("Exception", StringComparison.Ordinal) || l.Contains("Failed", StringComparison.OrdinalIgnoreCase) || l.Contains("corrupt", StringComparison.OrdinalIgnoreCase)))
            ? CrashDiagnosis.Of(CrashCause.BrokenWorld)
            : null;

    private static CrashDiagnosis? ModCrashed(List<string> lines) =>
        BlamedMods(lines) is { Count: > 0 } mods ? new CrashDiagnosis(CrashCause.ModCrashed, mods, []) : null;

    /// <summary>Mods the output blames: a failed mixin, a failed entrypoint, the crash report's "Suspected Mods".</summary>
    private static IReadOnlyList<string> BlamedMods(List<string> lines)
    {
        var ids = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            foreach (var match in new[] { MixinFailed().Match(line), MixinFromMod().Match(line), EntrypointFailed().Match(line) })
            {
                if (match.Success)
                {
                    ids.Add(match.Groups["id"].Value);
                }
            }

            if (SuspectedMods().Match(line) is { Success: true } suspected)
            {
                var list = suspected.Groups["list"].Value.Trim();
                if (list.Length > 0)
                {
                    ids.AddRange(SuspectedIds(list));
                }
                else
                {
                    // NeoForge lists them on the lines below, one indented "Name (id), Version: x" each.
                    for (var j = i + 1; j < lines.Count && lines[j].StartsWith('\t'); j++)
                    {
                        if (ModInParentheses().Match(lines[j]) is { Success: true } named && !lines[j].TrimStart().StartsWith("Issue", StringComparison.Ordinal))
                        {
                            ids.Add(named.Groups["id"].Value);
                        }
                    }
                }
            }
        }

        if (ids.Count == 0)
        {
            // Forge names each mod's class in a stack trace "TRANSFORMER/create@0.5.1/...": the top frame from a mod is a fair guess.
            ids.AddRange(lines.Select(l => ForgeFrame().Match(l)).Where(m => m.Success).Select(m => m.Groups["id"].Value)
                .Where(id => !NotMods.Contains(id)).Take(1));
        }

        return Distinct(ids.Where(id => !NotMods.Contains(id)));
    }

    private static IEnumerable<string> SuspectedIds(string list)
    {
        if (list.Equals("NONE", StringComparison.OrdinalIgnoreCase) || list.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var inParentheses = ModInParentheses().Matches(list).Select(m => m.Groups["id"].Value).ToList();
        return inParentheses.Count > 0 ? inParentheses : list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>"mod 'Fabric API' (fabric-api)" or "'Minecraft' (minecraft)" → the id; a bare id stays as it is.</summary>
    private static string IdIn(string text) => ModInParentheses().Match(text) is { Success: true } m ? m.Groups["id"].Value : text.Trim();

    private static IEnumerable<string> SplitFiles(string files) =>
        files.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(f => Path.GetFileName(f.Trim('[', ']', ' ')))
            .Where(f => f.Length > 0);

    private static List<string> Distinct(IEnumerable<string> items) => items.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    [GeneratedRegex("§.")]
    private static partial Regex FormattingCode();

    [GeneratedRegex(@"Starting Minecraft server on .*:(?<port>\d{1,5})\s*$")]
    private static partial Regex StartingOn();

    [GeneratedRegex(@"class file version (?<v>\d+)")]
    private static partial Regex ClassFileVersion();

    [GeneratedRegex(@"\d+")]
    private static partial Regex FirstNumber();

    [GeneratedRegex(@"Mod ID: '(?<id>[^']+)' from mod files: (?<files>.+)$")]
    private static partial Regex ForgeDuplicate();

    [GeneratedRegex(@"Mod (?<id>\S+) is present in multiple files: (?<files>.+)$")]
    private static partial Regex NeoForgeDuplicate();

    [GeneratedRegex(@"\bduplicate mod (?<id>[A-Za-z0-9_.-]+)")]
    private static partial Regex FabricDuplicate();

    [GeneratedRegex(@"\((?<mod>[^)]+)\) \S+ requires .+? of (?<dep>.+?), which is missing!")]
    private static partial Regex FabricMissing();

    [GeneratedRegex(@"\((?<mod>[^)]+)\) \S+ requires (?<wanted>.+?) of (?<dep>.+?), but only the wrong version")]
    private static partial Regex FabricWrongVersion();

    [GeneratedRegex(@"\((?<mod>[^)]+)\) \S+ requires .+? of (?<dep>.+?), which is disabled for this environment")]
    private static partial Regex FabricClientOnlyDependency();

    [GeneratedRegex(@"\((?<mod>[^)]+)\) \S+ is incompatible with .+? of (?<dep>.+?), yet")]
    private static partial Regex FabricBreaks();

    [GeneratedRegex(@"Mod ID: '(?<dep>[^']+)', Requested by: '(?<mod>[^']+)', Expected range: '(?<range>[^']*)', Actual version: '(?<actual>[^']*)'")]
    private static partial Regex ForgeDependency();

    [GeneratedRegex(@"^[\s-]*Mod (?<mod>[^'()]+?) requires (?<dep>[a-z0-9_.-]+)\b ?(?<wanted>.*)$")]
    private static partial Regex NeoForgeDependency();

    [GeneratedRegex(@"Mixin apply for mod (?<id>[A-Za-z0-9_.-]+) failed")]
    private static partial Regex MixinFailed();

    [GeneratedRegex(@"Mixin \[[^\]]*\] from mod (?<id>[A-Za-z0-9_.-]+) failed")]
    private static partial Regex MixinFromMod();

    [GeneratedRegex(@"entrypoint stage '[^']*' due to errors, provided by '(?<id>[^']+)'")]
    private static partial Regex EntrypointFailed();

    [GeneratedRegex(@"Suspected Mods?: ?(?<list>.*)$")]
    private static partial Regex SuspectedMods();

    [GeneratedRegex(@"\((?<id>[a-z0-9_.-]+)\)")]
    private static partial Regex ModInParentheses();

    [GeneratedRegex(@"TRANSFORMER/(?<id>[a-z0-9_]+)@")]
    private static partial Regex ForgeFrame();
}
