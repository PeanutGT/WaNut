using System.IO.Compression;
using WaNut.Core.Backups;
using WaNut.Core.Instances;
using WaNut.Core.Players;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Worlds;
using WaNut.Core.Worlds.Nbt;

namespace WaNut.Core.Tests.Worlds;

public sealed class WorldImportExportTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
    private readonly StubHttpHandler _http = new();
    private readonly BackupStore _backups;
    private readonly WorldImporter _importer;
    private readonly WorldExporter _exporter;

    public WorldImportExportTests()
    {
        _backups = new BackupStore(_clock);
        _importer = new WorldImporter(_backups, new MojangProfiles(_http.CreateClient()), _clock);
        _exporter = new WorldExporter(_backups, _clock);
        Directory.CreateDirectory(Server);
    }

    public void Dispose() => _temp.Dispose();

    private string Server => Path.Combine(_temp.Path, "server");

    private string NewSource(NbtCompound? player = null) =>
        WorldFixture.Create(Path.Combine(_temp.Path, "saves", "Island"), "Island", player: player);

    private void OfflineMode() => File.WriteAllText(Path.Combine(Server, "server.properties"), "online-mode=false\n");

    private Task<WorldImportResult> ImportAsync(WorldSource source, ServerType type = ServerType.Vanilla, string version = "1.21.1", string? owner = null) =>
        _importer.ImportAsync(Server, new WorldImportRequest(source, type, version, owner, owner is not null));

    [Fact]
    public async Task Import_FolderIntoNewServer_CopiesWorldAndLeavesSourceAlone()
    {
        var source = NewSource();

        var result = await ImportAsync(WorldScanner.FromFolder(source));

        Assert.True(File.Exists(Path.Combine(Server, "world", "level.dat")));
        Assert.True(File.Exists(Path.Combine(Server, "world", "region", "r.0.0.mca")));
        Assert.False(File.Exists(Path.Combine(Server, "world", "session.lock")));
        Assert.True(File.Exists(Path.Combine(source, "session.lock")));
        Assert.Null(result.SafetyBackup);
        Assert.Equal(new[] { "saves", "server" }, Directory.EnumerateDirectories(_temp.Path).Select(Path.GetFileName).Order());
        Assert.Equal(["world"], Directory.EnumerateDirectories(Server).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Import_OverExistingWorld_BacksItUpAndReplacesIt()
    {
        WorldFixture.Create(Path.Combine(Server, "world"), "Old");
        Directory.CreateDirectory(Path.Combine(Server, "world_nether"));

        var result = await ImportAsync(WorldScanner.FromFolder(NewSource()));

        Assert.Equal(BackupReason.Import, result.SafetyBackup?.Reason);
        Assert.Equal("Island", LevelData.ReadDetails(Path.Combine(Server, "world", "level.dat"), "x").LevelName);
        Assert.False(Directory.Exists(Path.Combine(Server, "world_nether")));
    }

    [Fact]
    public async Task Import_PaperLegacy_SplitsNetherAndEnd()
    {
        var source = NewSource();
        Directory.CreateDirectory(Path.Combine(source, "DIM-1", "region"));
        Directory.CreateDirectory(Path.Combine(source, "DIM1"));

        await ImportAsync(WorldScanner.FromFolder(source), ServerType.Paper, "1.21.1");

        Assert.True(Directory.Exists(Path.Combine(Server, "world_nether", "DIM-1", "region")));
        Assert.True(Directory.Exists(Path.Combine(Server, "world_the_end", "DIM1")));
        Assert.False(Directory.Exists(Path.Combine(Server, "world", "DIM-1")));
    }

    [Fact]
    public async Task Import_Paper26_KeepsDimensionsInside()
    {
        var source = NewSource();
        Directory.CreateDirectory(Path.Combine(source, "dimensions", "minecraft", "the_nether"));

        await ImportAsync(WorldScanner.FromFolder(source), ServerType.Paper, "26.3");

        Assert.True(Directory.Exists(Path.Combine(Server, "world", "dimensions", "minecraft", "the_nether")));
        Assert.False(Directory.Exists(Path.Combine(Server, "world_nether")));
    }

    [Fact]
    public async Task Import_OfflineHost_WritesInventoryToTheirPlayerFile()
    {
        OfflineMode();

        var result = await ImportAsync(WorldScanner.FromFolder(NewSource(WorldFixture.Player("diamond_sword"))), owner: "Peanut");

        Assert.True(result.InventoryMoved);
        var file = Path.Combine(Server, "world", "playerdata", PlayerIds.OfflineUuid("Peanut").ToString("D") + ".dat");
        Assert.Equal("diamond_sword", NbtFile.Read(file).Root.String("SelectedItem"));
    }

    [Fact]
    public async Task Import_OnlineHost_UsesMojangUuidAndNewPlayerFolderOn26()
    {
        _http.Add(MojangProfiles.ProfileUrl("Peanut"), """{"id":"069a79f444e94726a5befca90e38aaf5","name":"Peanut"}""");

        var result = await ImportAsync(WorldScanner.FromFolder(NewSource(WorldFixture.Player("bow"))), version: "26.3", owner: "Peanut");

        Assert.True(result.InventoryMoved);
        Assert.True(File.Exists(Path.Combine(Server, "world", "players", "data", "069a79f4-44e9-4726-a5be-fca90e38aaf5.dat")));
    }

    [Fact]
    public async Task Import_UnknownHostName_SkipsInventoryButImportsWorld()
    {
        var result = await ImportAsync(WorldScanner.FromFolder(NewSource(WorldFixture.Player("bow"))), owner: "Nobody");

        Assert.False(result.InventoryMoved);
        Assert.Equal(InventoryProblem.NameNotFound, result.InventoryProblem);
        Assert.True(File.Exists(Path.Combine(Server, "world", "level.dat")));
        Assert.False(Directory.Exists(Path.Combine(Server, "world", "playerdata")));
    }

    [Fact]
    public async Task Import_WorldWithoutHost_ReportsNoPlayer()
    {
        OfflineMode();

        var result = await ImportAsync(WorldScanner.FromFolder(NewSource()), owner: "Peanut");

        Assert.Equal(InventoryProblem.NoPlayerInWorld, result.InventoryProblem);
    }

    [Fact]
    public async Task Import_ZipWithFolderAround_ExtractsThatFolderAndNotesResourcePack()
    {
        var source = NewSource();
        File.WriteAllText(Path.Combine(source, "resources.zip"), "pack");
        Directory.CreateDirectory(Path.Combine(source, "datapacks", "fun"));
        File.WriteAllText(Path.Combine(source, "datapacks", "fun", "pack.mcmeta"), "{}");
        var zip = Path.Combine(_temp.Path, "map.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                archive.CreateEntryFromFile(file, "Adventure Map/" + Path.GetRelativePath(source, file).Replace('\\', '/'));
            }

            archive.CreateEntry("readme.txt");
        }

        var result = await ImportAsync(WorldScanner.FromZip(zip));

        Assert.True(result.HasResourcePack);
        Assert.True(File.Exists(Path.Combine(Server, "world", "datapacks", "fun", "pack.mcmeta")));
        Assert.False(File.Exists(Path.Combine(Server, "world", "session.lock")));
        Assert.False(File.Exists(Path.Combine(Server, "readme.txt")));
    }

    [Fact]
    public async Task Import_ZipThatClimbsOut_ChangesNothing()
    {
        WorldFixture.Create(Path.Combine(Server, "world"), "Old");
        var zip = Path.Combine(_temp.Path, "evil.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(Path.Combine(NewSource(), "level.dat"), "level.dat");
            using var writer = new StreamWriter(archive.CreateEntry("../escape.txt").Open());
            writer.Write("x");
        }

        var error = await Assert.ThrowsAsync<WorldException>(() => ImportAsync(WorldScanner.FromZip(zip)));

        Assert.Equal(WorldProblem.DamagedZip, error.Problem);
        Assert.False(File.Exists(Path.Combine(_temp.Path, "escape.txt")));
        Assert.Equal("Old", LevelData.ReadDetails(Path.Combine(Server, "world", "level.dat"), "x").LevelName);
        Assert.Equal(["world"], Directory.EnumerateDirectories(Server).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Export_PaperWorldToZip_JoinsDimensionsUnderWorldName()
    {
        WorldFixture.Create(Path.Combine(Server, "world"), "Server World");
        Directory.CreateDirectory(Path.Combine(Server, "world_nether", "DIM-1", "region"));
        File.WriteAllText(Path.Combine(Server, "world_nether", "DIM-1", "region", "r.0.0.mca"), "nether");
        var zip = Path.Combine(_temp.Path, "out.zip");

        var result = await _exporter.ExportAsync(Server, new WorldExportRequest(null, ExportTarget.Zip, zip, "My: Server", null));

        Assert.Equal(zip, result.Path);
        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("My_ Server/DIM-1/region/r.0.0.mca", names);
        Assert.Contains("My_ Server/level.dat", names);
        Assert.DoesNotContain(names, n => n.EndsWith("session.lock", StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(Server, "world_nether", "DIM-1")));
        Assert.Equal(["world", "world_nether"], Directory.EnumerateDirectories(Server).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Export_ToSavesWithPlayer_MakesThemTheHostAndNeverOverwrites()
    {
        WorldFixture.Create(Path.Combine(Server, "world"), "Server World", player: WorldFixture.Player("stale"));
        var uuid = Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(Server, "world", "playerdata"));
        NbtFile.Write(Path.Combine(Server, "world", "playerdata", uuid.ToString("D") + ".dat"), "", WorldFixture.Player("elytra"));
        var saves = Path.Combine(_temp.Path, "saves");
        Directory.CreateDirectory(Path.Combine(saves, "Survival"));

        var result = await _exporter.ExportAsync(Server, new WorldExportRequest(null, ExportTarget.Saves, saves, "Survival", uuid));

        Assert.Equal(Path.Combine(saves, "Survival (2)"), result.Path);
        Assert.Null(result.InventoryProblem);
        Assert.Equal("elytra", LevelData.ReadPlayer(Path.Combine(result.Path, "level.dat"))!.String("SelectedItem"));
        Assert.Equal("stale", LevelData.ReadPlayer(Path.Combine(Server, "world", "level.dat"))!.String("SelectedItem"));
        Assert.Equal(["Survival", "Survival (2)"], Directory.EnumerateDirectories(saves).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Export_FromBackup_UsesBackupWorldAndReportsMissingPlayer()
    {
        WorldFixture.Create(Path.Combine(Server, "world"), "Then");
        var backup = await _backups.CreateAsync(Server, BackupReason.Manual);
        WorldFixture.Create(Path.Combine(Server, "world"), "Now");
        var saves = Path.Combine(_temp.Path, "saves");

        var result = await _exporter.ExportAsync(Server, new WorldExportRequest(backup, ExportTarget.Saves, saves, "Old", Guid.NewGuid()));

        Assert.Equal("Then", LevelData.ReadDetails(Path.Combine(result.Path, "level.dat"), "x").LevelName);
        Assert.Equal(InventoryProblem.NoPlayerData, result.InventoryProblem);
    }

    [Fact]
    public async Task Export_NoWorldYet_Throws()
    {
        var error = await Assert.ThrowsAsync<WorldException>(() =>
            _exporter.ExportAsync(Server, new WorldExportRequest(null, ExportTarget.Zip, Path.Combine(_temp.Path, "x.zip"), "x", null)));

        Assert.Equal(WorldProblem.NoWorld, error.Problem);
    }

    [Fact]
    public async Task ExportThenImport_RoundTripsTheWorld()
    {
        WorldFixture.Create(Path.Combine(Server, "world"), "Round");
        var zip = Path.Combine(_temp.Path, "round.zip");
        await _exporter.ExportAsync(Server, new WorldExportRequest(null, ExportTarget.Zip, zip, "Round", null));
        Directory.Delete(Path.Combine(Server, "world"), recursive: true);

        await ImportAsync(WorldScanner.FromZip(zip));

        Assert.Equal("overworld", File.ReadAllText(Path.Combine(Server, "world", "region", "r.0.0.mca")));
    }
}
