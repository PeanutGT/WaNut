using System.IO.Compression;
using System.Text;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Modpacks;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Modpacks;

public sealed class ModpackInstallerTests : IDisposable
{
    private static readonly Uri JarUrl = new("https://cdn.modrinth.com/data/abc/versions/1/lithium.jar");
    private static readonly byte[] Jar = Encoding.UTF8.GetBytes("lithium");

    private readonly TempDir _dir = new();
    private readonly StubHttpHandler _http = new();
    private readonly ModpackInstaller _installer;

    public ModpackInstallerTests()
    {
        _installer = new ModpackInstaller(new FileDownloader(_http.CreateClient()));
        _http.Add(JarUrl, Jar);
        Directory.CreateDirectory(ServerDir);
    }

    public void Dispose() => _dir.Dispose();

    private string ServerDir => Path.Combine(_dir.Path, "server");

    private static string Sha512(byte[] data) => Convert.ToHexStringLower(System.Security.Cryptography.SHA512.HashData(data));

    private string WritePack(string files, params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_dir.Path, "pack.mrpack");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries.Prepend((ModpackReader.IndexFileName, ModpackReaderTests.Index(files))))
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(content);
        }

        return path;
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(ServerDir, relative));

    [Fact]
    public async Task InstallAsync_DownloadsFilesAndAppliesServerOverridesLast()
    {
        var path = WritePack(
            $"[{ModpackReaderTests.File(url: JarUrl.AbsoluteUri, sha512: Sha512(Jar))}]",
            ("overrides/config/lithium.properties", "client"),
            ("overrides/config/shared.txt", "shared"),
            ("server-overrides/config/lithium.properties", "server"),
            ("client-overrides/options.txt", "never"));

        await _installer.InstallAsync(ServerDir, path, ModpackReader.Read(path));

        Assert.Equal("lithium", Read("mods/lithium.jar"));
        Assert.Equal("server", Read("config/lithium.properties"));
        Assert.Equal("shared", Read("config/shared.txt"));
        Assert.False(File.Exists(Path.Combine(ServerDir, "options.txt")));
    }

    [Fact]
    public async Task InstallAsync_WrongHash_Throws()
    {
        var path = WritePack($"[{ModpackReaderTests.File(url: JarUrl.AbsoluteUri, sha512: new string('0', 128))}]");

        await Assert.ThrowsAsync<DownloadVerificationException>(() => _installer.InstallAsync(ServerDir, path, ModpackReader.Read(path)));
        Assert.False(File.Exists(Path.Combine(ServerDir, "mods", "lithium.jar")));
    }

    [Fact]
    public async Task InstallAsync_OverrideForWaNutFiles_IsSkipped()
    {
        File.WriteAllText(Path.Combine(ServerDir, InstanceMetadata.FileName), "mine");
        File.WriteAllText(Path.Combine(ServerDir, EulaFile.FileName), "eula=true");
        var path = WritePack("[]", ("overrides/instance.json", "theirs"), ("server-overrides/eula.txt", "eula=false"));

        await _installer.InstallAsync(ServerDir, path, ModpackReader.Read(path));

        Assert.Equal("mine", Read(InstanceMetadata.FileName));
        Assert.Equal("eula=true", Read(EulaFile.FileName));
    }

    [Theory]
    [InlineData("ops.json")]
    [InlineData("whitelist.json")]
    [InlineData("banned-players.json")]
    [InlineData("banned-ips.json")]
    [InlineData("usercache.json")]
    [InlineData("OPS.JSON")]
    public async Task InstallAsync_OverrideForPlayerAccessFile_IsSkipped(string name)
    {
        var path = WritePack("[]", ($"overrides/{name}", "[{\"name\":\"author\"}]"), ($"server-overrides/{name}", "[{\"name\":\"author\"}]"));

        await _installer.InstallAsync(ServerDir, path, ModpackReader.Read(path));

        Assert.Empty(Directory.GetFiles(ServerDir));
    }

    [Fact]
    public async Task InstallAsync_OverrideForExistingOps_KeepsTheServersOwn()
    {
        File.WriteAllText(Path.Combine(ServerDir, "ops.json"), "mine");
        var path = WritePack("[]", ("overrides/ops.json", "theirs"), ("overrides/config/ops.json", "a mod's own file"));

        await _installer.InstallAsync(ServerDir, path, ModpackReader.Read(path));

        Assert.Equal("mine", Read("ops.json"));
        Assert.Equal("a mod's own file", Read("config/ops.json"));
    }

    [Fact]
    public async Task InstallAsync_OverrideServerProperties_MergesGameSettingsButKeepsAccessAndPorts()
    {
        File.WriteAllText(Path.Combine(ServerDir, "server.properties"), "online-mode=true\nserver-port=25570\nwhite-list=true\ndifficulty=easy\nmotd=mine\n");
        var path = WritePack(
            "[]",
            ("overrides/server.properties", "online-mode=false\nserver-port=1\nwhite-list=false\nenable-rcon=true\nrcon.password=x\ndifficulty=hard\nallow-flight=true\n"),
            ("server-overrides/server.properties", "online-mode=false\nmax-players=40\n"));

        await _installer.InstallAsync(ServerDir, path, ModpackReader.Read(path));

        var properties = WaNut.Core.Properties.PropertiesFile.Load(Path.Combine(ServerDir, "server.properties"));
        Assert.Equal("true", properties.Get("online-mode"));
        Assert.Equal("25570", properties.Get("server-port"));
        Assert.Equal("true", properties.Get("white-list"));
        Assert.Null(properties.Get("enable-rcon"));
        Assert.Null(properties.Get("rcon.password"));
        Assert.Equal("hard", properties.Get("difficulty"));
        Assert.Equal("true", properties.Get("allow-flight"));
        Assert.Equal("40", properties.Get("max-players"));
        Assert.Equal("mine", properties.Get("motd"));
    }

    [Fact]
    public async Task InstallAsync_DownloadedFileNamedLikeAProtectedOne_IsSkipped()
    {
        var path = WritePack(
            $"[{ModpackReaderTests.File(path: "ops.json", url: JarUrl.AbsoluteUri, sha512: Sha512(Jar))},{ModpackReaderTests.File(path: "server.properties", url: JarUrl.AbsoluteUri, sha512: Sha512(Jar))}]");

        await _installer.InstallAsync(ServerDir, path, ModpackReader.Read(path));

        Assert.Empty(Directory.GetFiles(ServerDir));
    }

    [Fact]
    public async Task InstallAsync_OverrideOutsideServer_ThrowsUnsafeFile()
    {
        var path = WritePack("[]", ("overrides/../evil.txt", "x"));

        var e = await Assert.ThrowsAsync<ModpackException>(() => _installer.InstallAsync(ServerDir, path, ModpackReader.Read(path)));

        Assert.Equal(ModpackProblem.UnsafeFile, e.Problem);
        Assert.False(File.Exists(Path.Combine(_dir.Path, "evil.txt")));
    }

    [Fact]
    public async Task InstallAsync_ReportsProgressUpToOne()
    {
        var path = WritePack($"[{ModpackReaderTests.File(url: JarUrl.AbsoluteUri, sha512: Sha512(Jar))}]");
        var progress = new ListProgress<double>();

        await _installer.InstallAsync(ServerDir, path, ModpackReader.Read(path), progress);

        Assert.Equal(1, progress.Reports[^1]);
        Assert.All(progress.Reports, v => Assert.InRange(v, 0, 1));
    }
}
