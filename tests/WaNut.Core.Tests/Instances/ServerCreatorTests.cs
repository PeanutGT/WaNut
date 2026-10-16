using System.Text;
using Microsoft.Extensions.Time.Testing;
using WaNut.Core.Backups;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Java;
using WaNut.Core.Properties;
using WaNut.Core.Settings;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Versions;

namespace WaNut.Core.Tests.Instances;

public sealed class ServerCreatorTests : IDisposable
{
    private static readonly Uri DetailsUrl = new("https://piston-meta.mojang.com/v1/packages/1.21.1.json");
    private static readonly Uri JarUrl = new("https://piston-data.mojang.com/v1/objects/x/server.jar");
    private static readonly Uri ZipUrl = new("https://example.com/jre21.zip");
    private static readonly byte[] Jar = Encoding.UTF8.GetBytes("server jar bytes");
    private static readonly byte[] PaperJar = Encoding.UTF8.GetBytes("paper jar bytes");
    private static readonly Uri PaperJarUrl = new("https://fill-data.papermc.io/v1/objects/p/paper-1.21.1-133.jar");
    private static readonly byte[] FabricJar = SampleData.FabricServerLauncherJar();
    private static readonly Uri OldDetailsUrl = new("https://piston-meta.mojang.com/v1/packages/1.20.4.json");
    private static readonly Uri OldJarUrl = new("https://piston-data.mojang.com/v1/objects/y/server.jar");
    private static readonly byte[] OldJar = Encoding.UTF8.GetBytes("1.20.4 server jar bytes");
    private static readonly Uri Zip17Url = new("https://example.com/jre17.zip");
    private static readonly Uri NewDetailsUrl = new("https://piston-meta.mojang.com/v1/packages/1.21.10.json");
    private static readonly Uri NewJarUrl = new("https://piston-data.mojang.com/v1/objects/z/server.jar");
    private static readonly byte[] NewJar = Encoding.UTF8.GetBytes("1.21.10 server jar bytes");
    private static readonly byte[] ForgeInstallerJar = Encoding.UTF8.GetBytes("forge installer bytes");
    private static readonly byte[] NeoForgeInstallerJar = Encoding.UTF8.GetBytes("neoforge installer bytes");
    private const string ForgeArgs = "@libraries/net/minecraftforge/forge/1.21.1-52.0.38/win_args.txt";

    private readonly TempDir _temp = new();
    private readonly LauncherPaths _paths;
    private readonly StubHttpHandler _http;
    private readonly InstanceStore _store;
    private readonly GameVersion _version;
    private readonly GameVersion _oldVersion;
    private readonly GameVersion _newVersion;
    private readonly FakeForgeRunner _forgeRunner = new();

    public ServerCreatorTests()
    {
        _paths = new LauncherPaths(_temp.Path);
        _paths.EnsureCreated();
        _store = new InstanceStore(_paths);

        var details = Encoding.UTF8.GetBytes(SampleData.VersionDetails(JarUrl.AbsoluteUri, Hashes.Sha1(Jar), Jar.Length, javaMajor: 21));
        var zip = SampleData.JreZip(21);
        _http = new StubHttpHandler()
            .Add(DetailsUrl, details)
            .Add(JarUrl, Jar)
            .Add(AdoptiumClient.LatestJreUrl(21), SampleData.AdoptiumLatest(21, ZipUrl.AbsoluteUri, Hashes.Sha256(zip), zip.Length))
            .Add(ZipUrl, zip)
            .Add(PaperClient.BuildsUrl("1.21.1"), SampleData.PaperBuilds(
                (134, "ALPHA", "https://fill-data.papermc.io/v1/objects/x/paper-1.21.1-134.jar", new string('e', 64)),
                (133, "STABLE", PaperJarUrl.AbsoluteUri, Hashes.Sha256(PaperJar))))
            .Add(PaperJarUrl, PaperJar)
            .Add(FabricClient.LoaderVersionsUrl, SampleData.FabricLoaderVersions)
            .Add(FabricClient.InstallerVersionsUrl, SampleData.FabricInstallerVersions)
            .Add(FabricClient.ServerLauncherUrl("1.1.2"), FabricJar)
            .Add(new Uri(FabricClient.ServerLauncherUrl("1.1.2").AbsoluteUri + ".sha256"), Hashes.Sha256(FabricJar))
            .Add(ForgeClient.PromotionsUrl, """{"promos":{"1.21.1-latest":"52.0.40","1.21.1-recommended":"52.0.38"}}""")
            .Add(ForgeClient.InstallerUrl("1.21.1-52.0.38"), ForgeInstallerJar)
            .Add(new Uri(ForgeClient.InstallerUrl("1.21.1-52.0.38").AbsoluteUri + ".sha1"), Hashes.Sha1(ForgeInstallerJar))
            .Add(NeoForgeClient.VersionsUrl, """{"isSnapshot":false,"versions":["21.1.200","21.1.209","21.1.210-beta"]}""")
            .Add(NeoForgeClient.InstallerUrl("21.1.209"), NeoForgeInstallerJar)
            .Add(new Uri(NeoForgeClient.InstallerUrl("21.1.209").AbsoluteUri + ".sha1"), Hashes.Sha1(NeoForgeInstallerJar))
            .Add(NeoForgeClient.InstallerUrl("21.1.77"), NeoForgeInstallerJar)
            .Add(new Uri(NeoForgeClient.InstallerUrl("21.1.77").AbsoluteUri + ".sha1"), Hashes.Sha1(NeoForgeInstallerJar));
        _version = new GameVersion("1.21.1", DateTimeOffset.UnixEpoch, DetailsUrl, Hashes.Sha1(details));

        var oldDetails = Encoding.UTF8.GetBytes(SampleData.VersionDetails(OldJarUrl.AbsoluteUri, Hashes.Sha1(OldJar), OldJar.Length, javaMajor: 17));
        var zip17 = SampleData.JreZip(17);
        var newDetails = Encoding.UTF8.GetBytes(SampleData.VersionDetails(NewJarUrl.AbsoluteUri, Hashes.Sha1(NewJar), NewJar.Length, javaMajor: 21));
        _http
            .Add(OldDetailsUrl, oldDetails)
            .Add(OldJarUrl, OldJar)
            .Add(AdoptiumClient.LatestJreUrl(17), SampleData.AdoptiumLatest(17, Zip17Url.AbsoluteUri, Hashes.Sha256(zip17), zip17.Length))
            .Add(Zip17Url, zip17)
            .Add(NewDetailsUrl, newDetails)
            .Add(NewJarUrl, NewJar);
        _oldVersion = new GameVersion("1.20.4", DateTimeOffset.UnixEpoch, OldDetailsUrl, Hashes.Sha1(oldDetails));
        _newVersion = new GameVersion("1.21.10", DateTimeOffset.UnixEpoch, NewDetailsUrl, Hashes.Sha1(newDetails));
    }

    public void Dispose() => _temp.Dispose();

    private ServerCreator CreateCreator()
    {
        var client = _http.CreateClient();
        var downloader = new FileDownloader(client);
        var java = new JavaRuntimeManager(_paths, new AdoptiumClient(client), downloader);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        return new ServerCreator(
            _paths,
            new MojangVersionService(client, downloader),
            new PaperClient(client),
            new FabricClient(client),
            new ForgeClient(client),
            new NeoForgeClient(client),
            _forgeRunner,
            java,
            downloader,
            _store,
            new BackupStore(clock),
            clock);
    }

    [Fact]
    public async Task CreateAsync_EulaAccepted_CreatesReadyServerFolder()
    {
        var metadata = await CreateCreator().CreateAsync(new CreateServerRequest("My Server", _version, 4096, EulaAccepted: true));

        var dir = _store.InstanceDir(metadata.Id);
        Assert.Equal("my-server", metadata.Id);
        Assert.Equal(ServerType.Vanilla, metadata.Type);
        Assert.Equal("1.21.1", metadata.MinecraftVersion);
        Assert.Equal(21, metadata.JavaMajor);
        Assert.Equal(4096, metadata.MemoryMb);
        Assert.Equal(Jar, await File.ReadAllBytesAsync(Path.Combine(dir, "server.jar")));
        Assert.True(EulaFile.IsAccepted(dir));
        Assert.Contains("white-list=true", await File.ReadAllTextAsync(Path.Combine(dir, "server.properties")), StringComparison.Ordinal);
        Assert.Equivalent(metadata, Assert.Single(_store.LoadAll()), strict: true);
    }

    [Fact]
    public async Task CreateAsync_World_WritesSeedAndTypeForTheVersion()
    {
        var world = new WorldGeneration("peanut", WorldType.Superflat, FlatPreset.Void);
        var metadata = await CreateCreator().CreateAsync(new CreateServerRequest("Flat", _version, 2048, EulaAccepted: true, World: world));

        var properties = PropertiesFile.Load(Path.Combine(_store.InstanceDir(metadata.Id), "server.properties"));
        Assert.Equal("peanut", properties.Get("level-seed"));
        Assert.Equal("minecraft:flat", properties.Get("level-type"));
        Assert.Contains("minecraft:the_void", properties.Get("generator-settings")!, StringComparison.Ordinal);
        Assert.Equal("true", properties.Get("white-list"));
    }

    [Fact]
    public async Task CreateAsync_OwnerName_QueuesWhitelistAndOp()
    {
        var metadata = await CreateCreator().CreateAsync(new CreateServerRequest("Mine", _version, 2048, EulaAccepted: true, OwnerName: " Peanut_GT "));

        Assert.Equal(["whitelist add Peanut_GT", "op Peanut_GT"], metadata.PendingCommands);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task CreateAsync_OwnerSkipped_QueuesNothing(string? owner)
    {
        var metadata = await CreateCreator().CreateAsync(new CreateServerRequest("Mine", _version, 2048, EulaAccepted: true, OwnerName: owner));

        Assert.Empty(metadata.PendingCommands);
    }

    [Fact]
    public async Task CreateAsync_InvalidOwnerName_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateCreator().CreateAsync(new CreateServerRequest("Mine", _version, 2048, EulaAccepted: true, OwnerName: "op x; stop")));
    }

    [Fact]
    public async Task CreateAsync_Paper_UsesNewestStableBuildAsServerJar()
    {
        var metadata = await CreateCreator().CreateAsync(new CreateServerRequest("Paper", _version, 4096, EulaAccepted: true, ServerType.Paper));

        var dir = _store.InstanceDir(metadata.Id);
        Assert.Equal(ServerType.Paper, metadata.Type);
        Assert.Equal("133", metadata.LoaderVersion);
        Assert.Equal("server.jar", metadata.ServerJar);
        Assert.Equal(PaperJar, await File.ReadAllBytesAsync(Path.Combine(dir, "server.jar")));
        Assert.Equal(21, metadata.JavaMajor);
        Assert.DoesNotContain(JarUrl, _http.Requests);
    }

    [Fact]
    public async Task CreateAsync_PaperChecksumWrong_ThrowsAndCreatesNoServer()
    {
        _http.Add(PaperClient.BuildsUrl("1.21.1"), SampleData.PaperBuilds((133, "STABLE", PaperJarUrl.AbsoluteUri, new string('0', 64))));

        await Assert.ThrowsAsync<DownloadVerificationException>(() =>
            CreateCreator().CreateAsync(new CreateServerRequest("Paper", _version, 4096, EulaAccepted: true, ServerType.Paper)));

        Assert.Empty(Directory.GetDirectories(_paths.InstancesDir));
    }

    [Fact]
    public async Task CreateAsync_Fabric_PlacesLauncherNextToVanillaJar()
    {
        var metadata = await CreateCreator().CreateAsync(new CreateServerRequest("Fabric", _version, 4096, EulaAccepted: true, ServerType.Fabric));

        var dir = _store.InstanceDir(metadata.Id);
        Assert.Equal(ServerType.Fabric, metadata.Type);
        Assert.Equal("0.19.5", metadata.LoaderVersion);
        Assert.Equal(FabricClient.LauncherFileName, metadata.ServerJar);
        Assert.Equal(Jar, await File.ReadAllBytesAsync(Path.Combine(dir, "server.jar")));
        Assert.True(Directory.Exists(Path.Combine(dir, "mods")));

        using var launcher = System.IO.Compression.ZipFile.OpenRead(Path.Combine(dir, FabricClient.LauncherFileName));
        using var reader = new StreamReader(launcher.GetEntry("install.properties")!.Open());
        Assert.Equal("fabric-loader-version=0.19.5\ngame-version=1.21.1\n", reader.ReadToEnd());
    }

    [Fact]
    public async Task CreateAsync_FabricWithLoaderVersion_UsesThatLoader()
    {
        var metadata = await CreateCreator().CreateAsync(
            new CreateServerRequest("Pack", _version, 4096, EulaAccepted: true, ServerType.Fabric, LoaderVersion: "0.16.10"));

        Assert.Equal("0.16.10", metadata.LoaderVersion);
        using var launcher = System.IO.Compression.ZipFile.OpenRead(Path.Combine(_store.InstanceDir(metadata.Id), FabricClient.LauncherFileName));
        using var reader = new StreamReader(launcher.GetEntry("install.properties")!.Open());
        Assert.Equal("fabric-loader-version=0.16.10\ngame-version=1.21.1\n", reader.ReadToEnd());
    }

    [Fact]
    public async Task CreateAsync_FabricChecksumWrong_ThrowsAndCreatesNoServer()
    {
        _http.Add(new Uri(FabricClient.ServerLauncherUrl("1.1.2").AbsoluteUri + ".sha256"), new string('0', 64));

        await Assert.ThrowsAsync<DownloadVerificationException>(() =>
            CreateCreator().CreateAsync(new CreateServerRequest("Fabric", _version, 4096, EulaAccepted: true, ServerType.Fabric)));

        Assert.Empty(Directory.GetDirectories(_paths.InstancesDir));
    }

    [Fact]
    public async Task CreateAsync_FabricTwice_KeepsCachedLauncherUnchanged()
    {
        var creator = CreateCreator();
        await creator.CreateAsync(new CreateServerRequest("One", _version, 2048, EulaAccepted: true, ServerType.Fabric));
        _http.Requests.Clear();

        await creator.CreateAsync(new CreateServerRequest("Two", _version, 2048, EulaAccepted: true, ServerType.Fabric));

        // install.properties goes into each server's copy, so the cached jar still matches Maven's checksum.
        Assert.DoesNotContain(FabricClient.ServerLauncherUrl("1.1.2"), _http.Requests);
        Assert.DoesNotContain(JarUrl, _http.Requests);
    }

    [Fact]
    public async Task CreateAsync_SecondServerSameVersion_ReusesCachedDownloads()
    {
        var creator = CreateCreator();
        await creator.CreateAsync(new CreateServerRequest("One", _version, 2048, EulaAccepted: true));
        _http.Requests.Clear();

        await creator.CreateAsync(new CreateServerRequest("Two", _version, 2048, EulaAccepted: true));

        Assert.DoesNotContain(JarUrl, _http.Requests);
        Assert.DoesNotContain(ZipUrl, _http.Requests);
        Assert.Equal(2, _store.LoadAll().Count);
    }

    [Fact]
    public async Task CreateAsync_EulaNotAccepted_ThrowsAndCreatesNothing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateCreator().CreateAsync(new CreateServerRequest("My Server", _version, 4096, EulaAccepted: false)));

        Assert.Empty(Directory.GetDirectories(_paths.InstancesDir));
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task CreateAsync_JarChecksumWrong_ThrowsAndCreatesNoServer()
    {
        var details = Encoding.UTF8.GetBytes(SampleData.VersionDetails(JarUrl.AbsoluteUri, new string('0', 40), Jar.Length, javaMajor: 21));
        _http.Add(DetailsUrl, details);
        var version = _version with { DetailsSha1 = Hashes.Sha1(details) };

        await Assert.ThrowsAsync<DownloadVerificationException>(() =>
            CreateCreator().CreateAsync(new CreateServerRequest("My Server", version, 4096, EulaAccepted: true)));

        Assert.Empty(Directory.GetDirectories(_paths.InstancesDir));
    }

    [Fact]
    public async Task CreateAsync_WithProgress_ReportsStagesInOrder()
    {
        var stages = new List<CreateServerStage>();
        var progress = new InlineProgress(p =>
        {
            if (stages.Count == 0 || stages[^1] != p.Stage)
            {
                stages.Add(p.Stage);
            }
        });

        await CreateCreator().CreateAsync(new CreateServerRequest("My Server", _version, 4096, EulaAccepted: true), progress);

        Assert.Equal(
            [CreateServerStage.ReadingVersion, CreateServerStage.DownloadingJava, CreateServerStage.DownloadingServer, CreateServerStage.WritingFiles],
            stages);
    }

    private async Task<(InstanceMetadata Server, string Dir)> CreateWithWorldAsync(ServerType type = ServerType.Vanilla)
    {
        var server = await CreateCreator().CreateAsync(new CreateServerRequest("Changing", _version, 4096, EulaAccepted: true, type));
        var dir = _store.InstanceDir(server.Id);
        Directory.CreateDirectory(Path.Combine(dir, "world"));
        File.WriteAllText(Path.Combine(dir, "world", "level.dat"), "my world");
        Directory.CreateDirectory(Path.Combine(dir, "libraries", "old"));
        Directory.CreateDirectory(Path.Combine(dir, "versions", "1.21.1"));
        return (server, dir);
    }

    [Fact]
    public async Task ChangeAsync_VanillaToPaper_SwapsJarAndKeepsWorld()
    {
        var (server, dir) = await CreateWithWorldAsync();

        var changed = await CreateCreator().ChangeAsync(server, ServerType.Paper, _version);

        Assert.Equal(ServerType.Paper, changed.Type);
        Assert.Equal("133", changed.LoaderVersion);
        Assert.Equal("server.jar", changed.ServerJar);
        Assert.Equal(PaperJar, await File.ReadAllBytesAsync(Path.Combine(dir, "server.jar")));
        Assert.Equal("my world", File.ReadAllText(Path.Combine(dir, "world", "level.dat")));
        Assert.False(Directory.Exists(Path.Combine(dir, "libraries")));
        Assert.False(Directory.Exists(Path.Combine(dir, "versions")));
        Assert.Empty(Directory.GetFiles(dir, "*.wanut-new"));
        Assert.Equal(ServerType.Paper, _store.LoadAll().Single().Type);
    }

    [Fact]
    public async Task ChangeAsync_BacksUpTheWorldFirst()
    {
        var (server, dir) = await CreateWithWorldAsync();

        await CreateCreator().ChangeAsync(server, ServerType.Paper, _version);

        var backup = Assert.Single(new BackupStore(TimeProvider.System).List(dir));
        Assert.Equal(BackupReason.Change, backup.Reason);
        using var zip = System.IO.Compression.ZipFile.OpenRead(backup.FilePath);
        Assert.Contains(zip.Entries, e => e.FullName == "world/level.dat");
    }

    [Fact]
    public async Task ChangeAsync_PaperToFabric_AddsLauncherAndRemovesPaperCache()
    {
        var (server, dir) = await CreateWithWorldAsync(ServerType.Paper);
        Directory.CreateDirectory(Path.Combine(dir, "cache"));
        File.WriteAllText(Path.Combine(dir, "cache", "mojang_1.21.1.jar"), "x");
        Directory.CreateDirectory(Path.Combine(dir, "plugins"));

        var changed = await CreateCreator().ChangeAsync(server, ServerType.Fabric, _version);

        Assert.Equal(FabricClient.LauncherFileName, changed.ServerJar);
        Assert.Equal("0.19.5", changed.LoaderVersion);
        Assert.Equal(Jar, await File.ReadAllBytesAsync(Path.Combine(dir, "server.jar")));
        Assert.True(Directory.Exists(Path.Combine(dir, "mods")));
        Assert.True(Directory.Exists(Path.Combine(dir, "plugins")));
        Assert.False(Directory.Exists(Path.Combine(dir, "cache")));
        using var launcher = System.IO.Compression.ZipFile.OpenRead(Path.Combine(dir, FabricClient.LauncherFileName));
        using var reader = new StreamReader(launcher.GetEntry("install.properties")!.Open());
        Assert.Equal("fabric-loader-version=0.19.5\ngame-version=1.21.1\n", reader.ReadToEnd());
    }

    [Fact]
    public async Task ChangeAsync_FabricToVanilla_RemovesLauncherKeepsMods()
    {
        var (server, dir) = await CreateWithWorldAsync(ServerType.Fabric);
        File.WriteAllText(Path.Combine(dir, "mods", "some-mod.jar"), "mod");

        var changed = await CreateCreator().ChangeAsync(server, ServerType.Vanilla, _version);

        Assert.Equal("server.jar", changed.ServerJar);
        Assert.Null(changed.LoaderVersion);
        Assert.False(File.Exists(Path.Combine(dir, FabricClient.LauncherFileName)));
        Assert.True(File.Exists(Path.Combine(dir, "mods", "some-mod.jar")));
    }

    [Theory]
    [InlineData(ServerType.Vanilla)]
    [InlineData(ServerType.Fabric)]
    public async Task ChangeAsync_FromPaper_MovesNetherAndEndIntoTheWorld(ServerType target)
    {
        var (server, dir) = await CreateWithWorldAsync(ServerType.Paper);
        WriteRegion(dir, "world_nether", "DIM-1", "nether");
        WriteRegion(dir, "world_the_end", "DIM1", "end");
        WriteRegion(dir, "world", "DIM-1", "stale nether from an earlier switch");

        await CreateCreator().ChangeAsync(server, target, _version);

        Assert.Equal("nether", ReadRegion(dir, "world", "DIM-1"));
        Assert.Equal("end", ReadRegion(dir, "world", "DIM1"));
        Assert.False(Directory.Exists(Path.Combine(dir, "world_nether", "DIM-1")));
        Assert.False(Directory.Exists(Path.Combine(dir, "world_the_end", "DIM1")));
    }

    [Fact]
    public async Task ChangeAsync_ToPaper_MovesNetherAndEndIntoTheirOwnFolders()
    {
        var (server, dir) = await CreateWithWorldAsync();
        WriteRegion(dir, "world", "DIM-1", "nether");
        WriteRegion(dir, "world", "DIM1", "end");

        await CreateCreator().ChangeAsync(server, ServerType.Paper, _version);

        Assert.Equal("nether", ReadRegion(dir, "world_nether", "DIM-1"));
        Assert.Equal("end", ReadRegion(dir, "world_the_end", "DIM1"));
        Assert.False(Directory.Exists(Path.Combine(dir, "world", "DIM-1")));
        Assert.False(Directory.Exists(Path.Combine(dir, "world", "DIM1")));
    }

    [Fact]
    public async Task ChangeAsync_BetweenVanillaAndFabric_LeavesDimensionsInPlace()
    {
        var (server, dir) = await CreateWithWorldAsync();
        WriteRegion(dir, "world", "DIM-1", "nether");

        await CreateCreator().ChangeAsync(server, ServerType.Fabric, _version);

        Assert.Equal("nether", ReadRegion(dir, "world", "DIM-1"));
        Assert.False(Directory.Exists(Path.Combine(dir, "world_nether")));
    }

    private static void WriteRegion(string dir, string world, string dimension, string content)
    {
        var region = Path.Combine(dir, world, dimension, "region");
        Directory.CreateDirectory(region);
        File.WriteAllText(Path.Combine(region, "r.0.0.mca"), content);
    }

    private static string ReadRegion(string dir, string world, string dimension) =>
        File.ReadAllText(Path.Combine(dir, world, dimension, "region", "r.0.0.mca"));

    [Fact]
    public async Task ChangeAsync_OlderVersion_UsesItsJava()
    {
        var (server, dir) = await CreateWithWorldAsync();

        var changed = await CreateCreator().ChangeAsync(server, ServerType.Vanilla, _oldVersion);

        Assert.Equal("1.20.4", changed.MinecraftVersion);
        Assert.Equal(17, changed.JavaMajor);
        Assert.Equal(OldJar, await File.ReadAllBytesAsync(Path.Combine(dir, "server.jar")));
    }

    [Fact]
    public async Task ChangeAsync_PvpMovesFromPropertiesToGameRule()
    {
        var (server, dir) = await CreateWithWorldAsync();
        var path = Path.Combine(dir, "server.properties");
        File.WriteAllText(path, File.ReadAllText(path) + "pvp=false\n");

        var changed = await CreateCreator().ChangeAsync(server, ServerType.Vanilla, _newVersion);

        Assert.False(changed.GameRules["pvp"]);
        Assert.Contains("gamerule pvp false", GameSettingsStore.RuleCommands(changed));
    }

    [Fact]
    public async Task ChangeAsync_DownloadFails_ChangesNothing()
    {
        var (server, dir) = await CreateWithWorldAsync();
        _http.Add(PaperClient.BuildsUrl("1.21.1"), SampleData.PaperBuilds((133, "STABLE", PaperJarUrl.AbsoluteUri, new string('0', 64))));

        await Assert.ThrowsAsync<DownloadVerificationException>(() => CreateCreator().ChangeAsync(server, ServerType.Paper, _version));

        Assert.Equal(Jar, await File.ReadAllBytesAsync(Path.Combine(dir, "server.jar")));
        Assert.True(Directory.Exists(Path.Combine(dir, "libraries")));
        Assert.Empty(new BackupStore(TimeProvider.System).List(dir));
        Assert.Equal(ServerType.Vanilla, _store.LoadAll().Single().Type);
    }

    [Fact]
    public async Task ChangeAsync_SameTypeAndVersion_Throws()
    {
        var (server, _) = await CreateWithWorldAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => CreateCreator().ChangeAsync(server, ServerType.Vanilla, _version));
    }

    [Fact]
    public async Task ChangeAsync_HandEditedJarPath_NeverDeletesOutsideTheFolder()
    {
        var (server, dir) = await CreateWithWorldAsync();
        var outside = Path.Combine(_paths.InstancesDir, "keep-me.jar");
        File.WriteAllText(outside, "not WaNut's");

        await CreateCreator().ChangeAsync(server with { ServerJar = "../keep-me.jar" }, ServerType.Paper, _version);

        Assert.True(File.Exists(outside));
    }

    [Theory]
    [InlineData("1.21.1", "1.20.4", true)]
    [InlineData("26.3", "1.21.10", true)]
    [InlineData("1.21.1", "1.21.1", false)]
    [InlineData("1.21.1", "26.3", false)]
    [InlineData("1.21", "1.21.1", false)]
    public void IsDowngrade_ComparesVersionNumbers(string current, string target, bool expected)
    {
        var server = new InstanceMetadata
        {
            Id = "x",
            Name = "x",
            Type = ServerType.Vanilla,
            MinecraftVersion = current,
            JavaMajor = 21,
            MemoryMb = 2048,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };

        Assert.Equal(expected, ServerCreator.IsDowngrade(server, target));
    }

    [Fact]
    public async Task CreateAsync_Forge_RunsInstallerAndStartsFromArgumentFile()
    {
        var metadata = await CreateCreator().CreateAsync(new CreateServerRequest("Forge", _version, 4096, EulaAccepted: true, ServerType.Forge));

        var dir = _store.InstanceDir(metadata.Id);
        Assert.Equal(ServerType.Forge, metadata.Type);
        Assert.Equal("52.0.38", metadata.LoaderVersion);
        Assert.Equal(ForgeArgs, metadata.ServerJar);
        Assert.True(File.Exists(Path.Combine(dir, ForgeArgs[1..])));
        Assert.True(File.Exists(Path.Combine(dir, "run.bat")));
        Assert.True(Directory.Exists(Path.Combine(dir, "mods")));
        Assert.False(File.Exists(Path.Combine(dir, ".wanut-installed")));
        Assert.Empty(Directory.GetFiles(dir, "*.log"));
        Assert.True(EulaFile.IsAccepted(dir));
        var run = Assert.Single(_forgeRunner.Runs);
        Assert.EndsWith("forge-1.21.1-52.0.38-installer.jar", run.InstallerJar, StringComparison.Ordinal);
        Assert.EndsWith("java.exe", run.JavaExe, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_ForgeTwice_RunsInstallerOnce()
    {
        await CreateCreator().CreateAsync(new CreateServerRequest("One", _version, 4096, EulaAccepted: true, ServerType.Forge));
        var second = await CreateCreator().CreateAsync(new CreateServerRequest("Two", _version, 4096, EulaAccepted: true, ServerType.Forge));

        Assert.Single(_forgeRunner.Runs);
        Assert.True(File.Exists(Path.Combine(_store.InstanceDir(second.Id), ForgeArgs[1..])));
    }

    [Fact]
    public async Task CreateAsync_ForgeWithoutArgumentFile_RunsTheForgeJar()
    {
        _forgeRunner.LegacyJar = true;

        var metadata = await CreateCreator().CreateAsync(new CreateServerRequest("Old", _version, 4096, EulaAccepted: true, ServerType.Forge));

        Assert.Equal("forge-1.21.1-52.0.38.jar", metadata.ServerJar);
    }

    [Fact]
    public async Task CreateAsync_ForgeInstallerFails_ThrowsAndCreatesNoServer()
    {
        _forgeRunner.Fail = true;

        await Assert.ThrowsAsync<ForgeInstallException>(() =>
            CreateCreator().CreateAsync(new CreateServerRequest("Forge", _version, 4096, EulaAccepted: true, ServerType.Forge)));

        Assert.Empty(Directory.GetDirectories(_paths.InstancesDir));

        // A failed install is never reused: the next try runs the installer again.
        _forgeRunner.Fail = false;
        await CreateCreator().CreateAsync(new CreateServerRequest("Forge", _version, 4096, EulaAccepted: true, ServerType.Forge));
        Assert.Equal(2, _forgeRunner.Runs.Count);
    }

    [Fact]
    public async Task CreateAsync_ForgeInstallerChecksumWrong_ThrowsBeforeRunningIt()
    {
        _http.Add(new Uri(ForgeClient.InstallerUrl("1.21.1-52.0.38").AbsoluteUri + ".sha1"), new string('0', 40));

        await Assert.ThrowsAsync<DownloadVerificationException>(() =>
            CreateCreator().CreateAsync(new CreateServerRequest("Forge", _version, 4096, EulaAccepted: true, ServerType.Forge)));

        Assert.Empty(_forgeRunner.Runs);
    }

    [Fact]
    public async Task CreateAsync_NeoForge_UsesNewestStableVersion()
    {
        var metadata = await CreateCreator().CreateAsync(new CreateServerRequest("Neo", _version, 4096, EulaAccepted: true, ServerType.NeoForge));

        Assert.Equal("21.1.209", metadata.LoaderVersion);
        Assert.Equal("@libraries/net/neoforged/neoforge/21.1.209/win_args.txt", metadata.ServerJar);
    }

    [Fact]
    public async Task CreateAsync_NeoForgeWithLoaderVersion_InstallsThatVersion()
    {
        var metadata = await CreateCreator().CreateAsync(
            new CreateServerRequest("Pack", _version, 4096, EulaAccepted: true, ServerType.NeoForge, LoaderVersion: "21.1.77"));

        Assert.Equal("21.1.77", metadata.LoaderVersion);
        Assert.EndsWith("neoforge-21.1.77-installer.jar", Assert.Single(_forgeRunner.Runs).InstallerJar, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_Forge_ReportsInstallingStage()
    {
        var stages = new List<CreateServerStage>();
        var progress = new InlineProgress(p =>
        {
            if (stages.Count == 0 || stages[^1] != p.Stage)
            {
                stages.Add(p.Stage);
            }
        });

        await CreateCreator().CreateAsync(new CreateServerRequest("Forge", _version, 4096, EulaAccepted: true, ServerType.Forge), progress);

        Assert.Equal(
            [CreateServerStage.ReadingVersion, CreateServerStage.DownloadingJava, CreateServerStage.DownloadingServer, CreateServerStage.InstallingLoader, CreateServerStage.WritingFiles],
            stages);
    }

    [Fact]
    public async Task ChangeAsync_FabricToForge_MovesModsAsideAndInstallsForge()
    {
        var (server, dir) = await CreateWithWorldAsync(ServerType.Fabric);
        File.WriteAllText(Path.Combine(dir, "mods", "sodium.jar"), "fabric mod");

        var changed = await CreateCreator().ChangeAsync(server, ServerType.Forge, _version);

        Assert.Equal(ForgeArgs, changed.ServerJar);
        Assert.True(File.Exists(Path.Combine(dir, ForgeArgs[1..])));
        Assert.False(File.Exists(Path.Combine(dir, FabricClient.LauncherFileName)));
        Assert.False(Directory.Exists(Path.Combine(dir, "libraries", "old")));
        Assert.False(Directory.Exists(Path.Combine(dir, ".wanut-new")));
        Assert.True(File.Exists(Path.Combine(dir, "mods-fabric", "sodium.jar")));
        Assert.Empty(Directory.GetFiles(Path.Combine(dir, "mods")));
        Assert.Equal("my world", File.ReadAllText(Path.Combine(dir, "world", "level.dat")));
    }

    [Fact]
    public async Task ChangeAsync_ModsFolderNameTaken_PicksTheNextName()
    {
        var (server, dir) = await CreateWithWorldAsync(ServerType.Fabric);
        File.WriteAllText(Path.Combine(dir, "mods", "sodium.jar"), "fabric mod");
        Directory.CreateDirectory(Path.Combine(dir, "mods-fabric"));

        await CreateCreator().ChangeAsync(server, ServerType.NeoForge, _version);

        Assert.True(File.Exists(Path.Combine(dir, "mods-fabric-2", "sodium.jar")));
    }

    [Fact]
    public async Task ChangeAsync_ForgeToVanilla_RemovesForgeFilesKeepsMods()
    {
        var (server, dir) = await CreateWithWorldAsync(ServerType.Forge);
        File.WriteAllText(Path.Combine(dir, "mods", "create.jar"), "forge mod");

        var changed = await CreateCreator().ChangeAsync(server, ServerType.Vanilla, _version);

        Assert.Equal("server.jar", changed.ServerJar);
        Assert.Null(changed.LoaderVersion);
        Assert.Equal(Jar, await File.ReadAllBytesAsync(Path.Combine(dir, "server.jar")));
        Assert.False(Directory.Exists(Path.Combine(dir, "libraries")));
        Assert.False(File.Exists(Path.Combine(dir, "run.bat")));
        Assert.False(File.Exists(Path.Combine(dir, "user_jvm_args.txt")));
        Assert.True(File.Exists(Path.Combine(dir, "mods", "create.jar")));
    }

    [Theory]
    [InlineData(ServerType.Fabric, ServerType.Forge, true)]
    [InlineData(ServerType.Forge, ServerType.NeoForge, true)]
    [InlineData(ServerType.Forge, ServerType.Forge, false)]
    [InlineData(ServerType.Fabric, ServerType.Vanilla, false)]
    [InlineData(ServerType.Paper, ServerType.Forge, false)]
    public void MovesMods_OnlyBetweenModLoaders(ServerType from, ServerType to, bool expected) =>
        Assert.Equal(expected, ServerCreator.MovesMods(from, to));

    /// <summary>Writes what the Forge and NeoForge installers leave behind, without running Java.</summary>
    private sealed class FakeForgeRunner : ForgeInstallerRunner
    {
        public List<(string JavaExe, string InstallerJar, string TargetDir)> Runs { get; } = [];

        public bool Fail { get; set; }

        /// <summary>Like Forge for 1.16.5: a jar to run instead of an argument file.</summary>
        public bool LegacyJar { get; set; }

        public override Task RunAsync(string javaExe, string installerJar, string targetDir, CancellationToken cancellationToken = default)
        {
            Runs.Add((javaExe, installerJar, targetDir));
            var name = Path.GetFileName(installerJar);
            File.WriteAllText(Path.Combine(targetDir, name + ".log"), "log");
            if (Fail)
            {
                throw new ForgeInstallException(1, "Could not download a library.");
            }

            var neo = name.StartsWith("neoforge-", StringComparison.Ordinal);
            var version = name[(neo ? "neoforge-".Length : "forge-".Length)..^"-installer.jar".Length];
            File.WriteAllText(Path.Combine(targetDir, "run.bat"), "java @user_jvm_args.txt");
            File.WriteAllText(Path.Combine(targetDir, "user_jvm_args.txt"), "# -Xmx4G");
            if (LegacyJar)
            {
                File.WriteAllText(Path.Combine(targetDir, $"forge-{version}.jar"), "forge");
                return Task.CompletedTask;
            }

            var args = Path.Combine(targetDir, "libraries", neo ? "net/neoforged/neoforge" : "net/minecraftforge/forge", version, "win_args.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(args)!);
            File.WriteAllText(args, "-cp libraries/x.jar");
            return Task.CompletedTask;
        }
    }

    private sealed class InlineProgress(Action<CreateServerProgress> handler) : IProgress<CreateServerProgress>
    {
        public void Report(CreateServerProgress value) => handler(value);
    }
}
