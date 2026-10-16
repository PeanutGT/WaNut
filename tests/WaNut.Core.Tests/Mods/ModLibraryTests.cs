using System.Text;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Mods;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Mods;

public sealed class ModLibraryTests : IDisposable
{
    private const string Game = "1.21.1";
    private static readonly ModPlatform Fabric = ModPlatform.For(ServerType.Fabric)!;

    private readonly TempDir _dir = new();
    private readonly StubHttpHandler _http = new();
    private readonly ModLibrary _library;

    public ModLibraryTests()
    {
        var client = _http.CreateClient();
        _library = new ModLibrary(new ModrinthClient(client), new FileDownloader(client));
    }

    public void Dispose() => _dir.Dispose();

    private string ModsDir => Path.Combine(_dir.Path, "mods");

    /// <summary>Serves a project with one version whose jar holds <paramref name="content"/>.</summary>
    private byte[] AddProject(string projectId, string title, string content, string dependencies = "[]", string? sha512 = null)
    {
        var jar = Encoding.UTF8.GetBytes(content);
        var url = new Uri($"https://cdn.modrinth.com/data/{projectId}/{projectId}.jar");
        var version = ModrinthClientTests.Version(
            projectId + "-v", "release", "2026-10-01T00:00:00Z", fileName: projectId + ".jar", url: url.AbsoluteUri,
            sha512: sha512 ?? Sha512(jar), dependencies: dependencies).Replace("\"project_id\":\"proj\"", $"\"project_id\":\"{projectId}\"", StringComparison.Ordinal);
        _http.Add(ModrinthClient.VersionsUrl(projectId, Fabric, Game), $"[{version}]")
            .Add(ModrinthClient.ProjectUrl(projectId), $$"""{"title":"{{title}}"}""")
            .Add(url, jar);
        return jar;
    }

    private static string Sha512(byte[] data) => Convert.ToHexStringLower(System.Security.Cryptography.SHA512.HashData(data));

    [Fact]
    public async Task InstallAsync_WithRequiredDependency_InstallsBothAndRecordsNames()
    {
        AddProject("api", "Fabric API", "api");
        AddProject("mod", "Cool Mod", "mod", dependencies: """[{"project_id":"api","dependency_type":"required"}]""");

        var result = await _library.InstallAsync(_dir.Path, Fabric, Game, "mod", "Cool Mod");

        Assert.Equal(["Cool Mod", "Fabric API"], result.Installed);
        Assert.Empty(result.MissingDependencies);
        Assert.Equal(["Cool Mod", "Fabric API"], _library.ListInstalled(_dir.Path, Fabric).Select(m => m.DisplayName));
    }

    [Fact]
    public async Task InstallAsync_DependencyAlreadyInstalled_IsNotDownloadedAgain()
    {
        AddProject("api", "Fabric API", "api");
        AddProject("mod", "Cool Mod", "mod", dependencies: """[{"project_id":"api","dependency_type":"required"}]""");
        await _library.InstallAsync(_dir.Path, Fabric, Game, "api", "Fabric API");
        _http.Requests.Clear();

        var result = await _library.InstallAsync(_dir.Path, Fabric, Game, "mod", "Cool Mod");

        Assert.Equal(["Cool Mod"], result.Installed);
        Assert.DoesNotContain(_http.Requests, u => u.AbsoluteUri.EndsWith("api.jar", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InstallAsync_DependencyCopiedInByHand_IsRecognisedByHash()
    {
        var api = AddProject("api", "Fabric API", "api");
        AddProject("mod", "Cool Mod", "mod", dependencies: """[{"project_id":"api","dependency_type":"required"}]""");
        Directory.CreateDirectory(ModsDir);
        File.WriteAllBytes(Path.Combine(ModsDir, "fabric-api-by-hand.jar"), api);
        _http.Add(ModrinthClient.VersionFilesUrl, $$$"""{"{{{Sha512(api)}}}":{"id":"api-v","project_id":"api"}}""");

        var result = await _library.InstallAsync(_dir.Path, Fabric, Game, "mod", "Cool Mod");

        Assert.Equal(["Cool Mod"], result.Installed);
        Assert.False(File.Exists(Path.Combine(ModsDir, "api.jar")));
    }

    [Fact]
    public async Task InstallAsync_DependencyWithoutVersion_IsReportedMissing()
    {
        AddProject("mod", "Cool Mod", "mod", dependencies: """[{"project_id":"gone","dependency_type":"required"}]""");
        _http.Add(ModrinthClient.VersionsUrl("gone", Fabric, Game), "[]").Add(ModrinthClient.ProjectUrl("gone"), """{"title":"Old Lib"}""");

        var result = await _library.InstallAsync(_dir.Path, Fabric, Game, "mod", "Cool Mod");

        Assert.Equal(["Cool Mod"], result.Installed);
        Assert.Equal(["Old Lib"], result.MissingDependencies);
    }

    [Fact]
    public async Task InstallAsync_ProjectWithoutVersion_ThrowsAndWritesNothing()
    {
        _http.Add(ModrinthClient.VersionsUrl("mod", Fabric, Game), "[]");

        await Assert.ThrowsAsync<NoCompatibleVersionException>(() => _library.InstallAsync(_dir.Path, Fabric, Game, "mod", "Cool Mod"));

        Assert.False(Directory.Exists(ModsDir));
    }

    [Fact]
    public async Task InstallAsync_HashMismatch_LeavesNoFile()
    {
        AddProject("mod", "Cool Mod", "mod", sha512: new string('0', 128));

        await Assert.ThrowsAsync<DownloadVerificationException>(() => _library.InstallAsync(_dir.Path, Fabric, Game, "mod", "Cool Mod"));

        Assert.Empty(Directory.GetFiles(ModsDir));
    }

    [Fact]
    public async Task Remove_InstalledMod_DeletesFileAndRecord()
    {
        AddProject("mod", "Cool Mod", "mod");
        await _library.InstallAsync(_dir.Path, Fabric, Game, "mod", "Cool Mod");

        _library.Remove(_dir.Path, Fabric, _library.ListInstalled(_dir.Path, Fabric)[0]);

        Assert.Empty(_library.ListInstalled(_dir.Path, Fabric));
        Assert.DoesNotContain("mod.jar", File.ReadAllText(Path.Combine(_dir.Path, ModLibrary.RecordsFileName)), StringComparison.Ordinal);
    }

    [Fact]
    public void ListInstalled_JarCopiedInByHand_ShowsFileName()
    {
        Directory.CreateDirectory(ModsDir);
        File.WriteAllText(Path.Combine(ModsDir, "lithium.jar"), "x");
        File.WriteAllText(Path.Combine(ModsDir, "notes.txt"), "x");

        var mod = Assert.Single(_library.ListInstalled(_dir.Path, Fabric));

        Assert.Equal("lithium.jar", mod.DisplayName);
        Assert.Null(mod.ProjectId);
    }

    [Fact]
    public void Remove_PathOutsideFolder_Throws()
    {
        Assert.Throws<ArgumentException>(() => _library.Remove(_dir.Path, Fabric, new InstalledMod("../server.jar")));
    }

    /// <summary>Writes a jar into a single-player instance's mods folder and returns its path.</summary>
    private string ClientJar(string fileName, byte[] content)
    {
        var dir = Path.Combine(_dir.Path, "client", "mods");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public async Task InstallCopiesAsync_MixedInstance_InstallsServerModsAndReportsTheRest()
    {
        var api = AddProject("api", "Fabric API", "api-server");
        _http.Add(ModrinthClient.VersionsUrl("old", Fabric, Game), "[]");
        var clientApi = Encoding.UTF8.GetBytes("api-client");
        var sodium = Encoding.UTF8.GetBytes("sodium");
        var old = Encoding.UTF8.GetBytes("old");
        string[] jars =
        [
            ClientJar("fabric-api-client.jar", clientApi),
            ClientJar("sodium.jar", sodium),
            ClientJar("old.jar", old),
            ClientJar("homemade.jar", Encoding.UTF8.GetBytes("homemade")),
        ];
        _http.Add(ModrinthClient.VersionFilesUrl, $$$"""
            {"{{{Sha512(clientApi)}}}":{"project_id":"api"},"{{{Sha512(sodium)}}}":{"project_id":"sodium"},"{{{Sha512(old)}}}":{"project_id":"old"}}
            """)
            .Add(ModrinthClient.ProjectsUrl(["api", "sodium", "old"]), """
            [{"id":"api","title":"Fabric API","server_side":"required"},
             {"id":"sodium","title":"Sodium","server_side":"unsupported"},
             {"id":"old","title":"Old Mod","server_side":"optional"}]
            """);

        var result = await _library.InstallCopiesAsync(_dir.Path, Fabric, Game, jars);

        Assert.Equal(["Fabric API"], result.Installed);
        Assert.Equal(["Old Mod"], result.NoServerVersion);
        Assert.Equal(["Sodium"], result.ClientOnly);
        Assert.Equal(["homemade.jar"], result.NotOnModrinth);

        // The server gets Modrinth's file for its own version, never the client's copy.
        Assert.Equal(api, File.ReadAllBytes(Path.Combine(ModsDir, "api.jar")));
        Assert.Equal(["api.jar"], Directory.GetFiles(ModsDir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task InstallCopiesAsync_NoJars_AsksModrinthNothing()
    {
        var result = await _library.InstallCopiesAsync(_dir.Path, Fabric, Game, []);

        Assert.Empty(result.Installed);
        Assert.Empty(result.NotOnModrinth);
        Assert.Empty(_http.Requests);
    }
}
