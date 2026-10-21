using WaNut.Core.Crashes;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Crashes;

public class CrashAnalyzerTests
{
    private static CrashDiagnosis? Analyze(string output, string? report = null) => CrashAnalyzer.Analyze(output.Split('\n'), report);

    [Fact]
    public void Analyze_VanillaPortInUse_ReturnsPortInUseWithPort()
    {
        var result = Analyze("""
            [12:00:01] [Server thread/INFO]: Starting Minecraft server on *:25565
            [12:00:01] [Server thread/WARN]: **** FAILED TO BIND TO PORT!
            [12:00:01] [Server thread/WARN]: The exception was: java.net.BindException: Address already in use: bind
            [12:00:01] [Server thread/WARN]: Perhaps a server is already running on that port?
            """);

        Assert.Equal(CrashDiagnosis.Of(CrashCause.PortInUse, "25565"), result, DiagnosisComparer.Instance);
    }

    [Fact]
    public void Analyze_FabricMissingFabricApi_ReturnsMissingModAndWhoNeedsIt()
    {
        var result = Analyze("""
            [12:00:01] [main/ERROR]: Incompatible mods found!
            net.fabricmc.loader.impl.FormattedException: Some of your mods are incompatible with the game or each other!
            A potential solution has been determined, this may resolve your problem:
            	 - Install fabric-api, any version.
            More details:
            	 - Mod 'Lithium' (lithium) 0.11.2 requires any version of fabric-api, which is missing!
            	 - Mod 'Mod Menu' (modmenu) 7.2.2 requires version 0.83.0 or later of fabric-api, which is missing!
            """);

        Assert.NotNull(result);
        Assert.Equal(CrashCause.MissingMod, result.Cause);
        Assert.Equal(["fabric-api"], result.ModIds);
        Assert.Equal("lithium", result.Detail);
    }

    [Fact]
    public void Analyze_FabricModForAnotherMinecraft_ReturnsIncompatibleMod()
    {
        var result = Analyze("""
            	 - Mod 'Fabric API' (fabric-api) 0.92.0+1.20.4 requires version 1.20.4 or later of 'Minecraft' (minecraft), but only the wrong version is present: 1.20.1!
            """);

        Assert.NotNull(result);
        Assert.Equal(CrashCause.IncompatibleMod, result.Cause);
        Assert.Equal(["fabric-api"], result.ModIds);
        Assert.Equal("minecraft", result.Detail);
    }

    [Fact]
    public void Analyze_FabricModNeedsNewerJava_ReturnsWrongJava()
    {
        var result = Analyze("""
            	 - Mod 'Sodium' (sodium) 0.6.0 requires version 21 or later of 'OpenJDK 64-Bit Server VM' (java), but only the wrong version is present: 17!
            """);

        Assert.NotNull(result);
        Assert.Equal(CrashCause.WrongJava, result.Cause);
        Assert.Equal(["sodium"], result.ModIds);
        Assert.Equal("21", result.Detail);
    }

    [Fact]
    public void Analyze_FabricDependsOnClientOnlyMod_ReturnsClientOnlyMod()
    {
        var result = Analyze("""
            	 - Mod 'Sodium Extra' (sodium-extra) 0.5.1 requires any version of mod 'Sodium' (sodium), which is disabled for this environment (client/server only)!
            """);

        Assert.NotNull(result);
        Assert.Equal(CrashCause.ClientOnlyMod, result.Cause);
        Assert.Equal(["sodium-extra"], result.ModIds);
    }

    [Fact]
    public void Analyze_ForgeDuplicateMods_ReturnsIdAndFiles()
    {
        var result = Analyze("""
            [12:00:01] [main/ERROR] [net.minecraftforge.fml.loading.ModSorter/LOADING]: Found duplicate mods:
            	Mod ID: 'jei' from mod files: jei-1.20.1-forge-15.2.0.27.jar, jei-1.20.1-forge-15.3.0.4.jar
            """);

        Assert.NotNull(result);
        Assert.Equal(CrashCause.DuplicateMod, result.Cause);
        Assert.Equal(["jei"], result.ModIds);
        Assert.Equal(["jei-1.20.1-forge-15.2.0.27.jar", "jei-1.20.1-forge-15.3.0.4.jar"], result.Files);
    }

    [Fact]
    public void Analyze_NeoForgeDuplicateWithFormattingCodes_ReturnsIdAndFiles()
    {
        var result = Analyze("\t- Mod §ecreate§r is present in multiple files: create-6.0.0.jar, create-6.0.1.jar");

        Assert.NotNull(result);
        Assert.Equal(CrashCause.DuplicateMod, result.Cause);
        Assert.Equal(["create"], result.ModIds);
        Assert.Equal(["create-6.0.0.jar", "create-6.0.1.jar"], result.Files);
    }

    [Fact]
    public void Analyze_ForgeMissingDependency_ReturnsMissingMod()
    {
        var result = Analyze("""
            [12:00:01] [main/ERROR] [net.minecraftforge.fml.loading.ModSorter/LOADING]: Missing or unsupported mandatory dependencies:
            	Mod ID: 'cloth_config', Requested by: 'xaerominimap', Expected range: '[11,)', Actual version: '[MISSING]'
            	Mod ID: 'minecraft', Requested by: 'xaerominimap', Expected range: '[1.20.4,1.21)', Actual version: '1.20.1'
            """);

        Assert.NotNull(result);
        Assert.Equal(CrashCause.MissingMod, result.Cause);
        Assert.Equal(["cloth_config"], result.ModIds);
        Assert.Equal("xaerominimap", result.Detail);
    }

    [Fact]
    public void Analyze_ForgeWrongMinecraftOnly_ReturnsIncompatibleMod()
    {
        var result = Analyze("""
            	Mod ID: 'minecraft', Requested by: 'create', Expected range: '[1.20.4,1.21)', Actual version: '1.20.1'
            """);

        Assert.NotNull(result);
        Assert.Equal(CrashCause.IncompatibleMod, result.Cause);
        Assert.Equal(["create"], result.ModIds);
        Assert.Equal("minecraft", result.Detail);
    }

    [Fact]
    public void Analyze_NeoForgeMissingDependency_ReturnsMissingMod()
    {
        var result = Analyze("""
            	- Mod §eCreate§r requires §6ponder§r §o1.0 or above§r
            	§7Currently, §6ponder§r§7 is §onot installed§r
            """);

        Assert.NotNull(result);
        Assert.Equal(CrashCause.MissingMod, result.Cause);
        Assert.Equal(["ponder"], result.ModIds);
    }

    [Fact]
    public void Analyze_ForgeClientClassOnServer_ReturnsClientOnlyModWithMixinMod()
    {
        var result = Analyze("""
            [12:00:01] [main/ERROR] [mixin/]: Mixin apply for mod journeymap failed journeymap.mixins.json:MinecraftMixin from mod journeymap
            java.lang.RuntimeException: Attempted to load class net/minecraft/client/Minecraft for invalid dist DEDICATED_SERVER
            """);

        Assert.NotNull(result);
        Assert.Equal(CrashCause.ClientOnlyMod, result.Cause);
        Assert.Equal(["journeymap"], result.ModIds);
    }

    [Fact]
    public void Analyze_JavaTooOld_ReturnsWrongJavaWithVersion()
    {
        var result = Analyze("""
            Error: LinkageError occurred while loading main class net.minecraft.bundler.Main
            	java.lang.UnsupportedClassVersionError: net/minecraft/bundler/Main has been compiled by a more recent version of the Java Runtime (class file version 65.0), this version of the Java Runtime only recognizes class file versions up to 61.0
            """);

        Assert.NotNull(result);
        Assert.Equal(CrashCause.WrongJava, result.Cause);
        Assert.Equal("21", result.Detail);
    }

    [Fact]
    public void Analyze_HeapTooBig_ReturnsMemoryTooHigh()
    {
        var result = Analyze("""
            Error occurred during initialization of VM
            Could not reserve enough space for 16777216KB object heap
            """);

        Assert.Equal(CrashCause.MemoryTooHigh, result?.Cause);
    }

    [Fact]
    public void Analyze_OutOfMemory_ReturnsOutOfMemoryThatMayNotRepeat()
    {
        var result = Analyze("""
            [12:30:00] [Server thread/ERROR]: Encountered an unexpected exception
            java.lang.OutOfMemoryError: Java heap space
            """);

        Assert.NotNull(result);
        Assert.Equal(CrashCause.OutOfMemory, result.Cause);
        Assert.False(result.WillRepeat);
    }

    [Fact]
    public void Analyze_BrokenLevelDat_ReturnsBrokenWorld()
    {
        var result = Analyze("[12:00:01] [main/ERROR]: Exception reading ./world/level.dat");

        Assert.Equal(CrashCause.BrokenWorld, result?.Cause);
    }

    [Fact]
    public void Analyze_CrashReportSuspectsMod_ReturnsModCrashed()
    {
        var report = """
            ---- Minecraft Crash Report ----
            Description: Ticking entity
            Suspected Mods:
            	Create (create), Version: 6.0.1
            		Issue tracker URL: https://github.com/Creators-of-Create/Create/issues
            """;

        var result = Analyze("[12:30:00] [Server thread/ERROR]: This crash report has been saved to: ./crash-reports/crash.txt", report);

        Assert.NotNull(result);
        Assert.Equal(CrashCause.ModCrashed, result.Cause);
        Assert.Equal(["create"], result.ModIds);
    }

    [Fact]
    public void Analyze_FabricEntrypointFailed_ReturnsModCrashed()
    {
        var result = Analyze("java.lang.RuntimeException: Could not execute entrypoint stage 'main' due to errors, provided by 'lithium' at 'net.caffeinemc.Lithium'!");

        Assert.Equal(["lithium"], result?.ModIds);
    }

    [Fact]
    public void Analyze_PlayerTypesAnError_Ignored()
    {
        var result = Analyze("""
            [12:00:01] [Server thread/INFO]: <Steve> **** FAILED TO BIND TO PORT!
            [12:00:01] [Server thread/INFO]: [Not Secure] <Alex> java.lang.OutOfMemoryError
            """);

        Assert.Null(result);
    }

    [Fact]
    public void Analyze_NormalStop_ReturnsNull()
    {
        var result = Analyze("""
            [12:00:01] [Server thread/INFO]: Done (3.2s)! For help, type "help"
            [12:10:00] [Server thread/INFO]: Stopping server
            [12:10:00] [Server thread/INFO]: Saving worlds
            """, "Suspected Mods: NONE");

        Assert.Null(result);
    }

    [Fact]
    public void FindCrashReport_NewAndOldReports_ReturnsNewestSinceStart()
    {
        using var dir = new TempDir();
        var folder = Directory.CreateDirectory(Path.Combine(dir.Path, "crash-reports")).FullName;
        var start = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(Path.Combine(folder, "old.txt"), "x");
        File.SetLastWriteTimeUtc(Path.Combine(folder, "old.txt"), start.AddHours(-1));
        File.WriteAllText(Path.Combine(folder, "new.txt"), "x");
        File.SetLastWriteTimeUtc(Path.Combine(folder, "new.txt"), start.AddMinutes(5));

        Assert.Equal(Path.Combine(folder, "new.txt"), CrashAnalyzer.FindCrashReport(dir.Path, start));
        Assert.Null(CrashAnalyzer.FindCrashReport(dir.Path, start.AddHours(1)));
    }

    /// <summary>Records holding lists compare by reference; this compares their items.</summary>
    private sealed class DiagnosisComparer : IEqualityComparer<CrashDiagnosis?>
    {
        public static readonly DiagnosisComparer Instance = new();

        public bool Equals(CrashDiagnosis? x, CrashDiagnosis? y) =>
            x is not null && y is not null && x.Cause == y.Cause && x.Detail == y.Detail
            && x.ModIds.SequenceEqual(y.ModIds) && x.Files.SequenceEqual(y.Files);

        public int GetHashCode(CrashDiagnosis? obj) => obj?.Cause.GetHashCode() ?? 0;
    }
}
