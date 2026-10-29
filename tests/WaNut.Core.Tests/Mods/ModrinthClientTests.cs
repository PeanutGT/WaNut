using System.Text;
using WaNut.Core.Instances;
using WaNut.Core.Mods;

namespace WaNut.Core.Tests.Mods;

public sealed class ModrinthClientTests
{
    private static readonly ModPlatform Fabric = ModPlatform.For(ServerType.Fabric)!;
    private static readonly ModPlatform Paper = ModPlatform.For(ServerType.Paper)!;

    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));

    internal static string Version(
        string id, string type, string date, string fileName = "mod.jar", string url = "https://cdn.modrinth.com/data/p/mod.jar",
        string sha512 = "abc", string loader = "fabric", string game = "1.21.1", string dependencies = "[]", bool primary = true) => $$$"""
        {"id":"{{{id}}}","project_id":"proj","version_number":"{{{id}}}","version_type":"{{{type}}}","date_published":"{{{date}}}",
         "game_versions":["{{{game}}}"],"loaders":["{{{loader}}}"],
         "files":[{"url":"{{{url}}}","filename":"{{{fileName}}}","primary":{{{(primary ? "true" : "false")}}},"hashes":{"sha1":"x","sha512":"{{{sha512}}}"}}],
         "dependencies":{{{dependencies}}}}
        """;

    [Fact]
    public void For_Vanilla_ReturnsNull()
    {
        Assert.Null(ModPlatform.For(ServerType.Vanilla));
    }

    [Fact]
    public void For_Paper_UsesPluginsFolderAndBukkitFamilyLoaders()
    {
        Assert.Equal("plugins", Paper.FolderName);
        Assert.Equal(["paper", "spigot", "bukkit"], Paper.Loaders);
    }

    [Fact]
    public void SearchUrl_Fabric_FiltersLoaderVersionAndServerSide()
    {
        var url = Uri.UnescapeDataString(ModrinthClient.SearchUrl(Fabric, "1.21.1", " sodium ", 40).AbsoluteUri);

        Assert.Contains("query=sodium&", url, StringComparison.Ordinal);
        Assert.Contains("""["categories:fabric"]""", url, StringComparison.Ordinal);
        Assert.Contains("""["versions:1.21.1"]""", url, StringComparison.Ordinal);
        Assert.Contains("""["server_side:required","server_side:optional"]""", url, StringComparison.Ordinal);
        Assert.Contains("index=relevance&offset=40&limit=20", url, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchUrl_PaperWithEmptyQuery_SortsByDownloadsWithoutServerSideFacet()
    {
        var url = Uri.UnescapeDataString(ModrinthClient.SearchUrl(Paper, "1.21.4", "", 0).AbsoluteUri);

        Assert.Contains("""["categories:paper","categories:spigot","categories:bukkit"]""", url, StringComparison.Ordinal);
        Assert.DoesNotContain("server_side", url, StringComparison.Ordinal);
        Assert.Contains("index=downloads", url, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseSearch_Hits_ReadsFieldsAndTotal()
    {
        var page = ModrinthClient.ParseSearch(Json("""
            {"hits":[{"project_id":"AANobbMI","slug":"sodium","title":"Sodium","description":"Fast","author":"jellysquid3","downloads":123,"project_type":"mod"}],
             "offset":0,"limit":20,"total_hits":57}
            """));

        var hit = Assert.Single(page.Hits);
        Assert.Equal(57, page.TotalHits);
        Assert.Equal("Sodium", hit.Title);
        Assert.Equal("https://modrinth.com/mod/sodium", hit.PageUrl.AbsoluteUri);
    }

    [Fact]
    public void ParseBestVersion_NewerBetaAndOlderRelease_PicksRelease()
    {
        var json = $"[{Version("beta", "beta", "2026-10-01T00:00:00Z")},{Version("old", "release", "2026-01-01T00:00:00Z")},{Version("new", "release", "2026-05-01T00:00:00Z")}]";

        Assert.Equal("new", ModrinthClient.ParseBestVersion(Json(json), Fabric, "1.21.1")!.VersionId);
    }

    [Fact]
    public void ParseBestVersion_OnlyBeta_PicksBeta()
    {
        var json = $"[{Version("beta", "beta", "2026-10-01T00:00:00Z")}]";

        Assert.Equal("beta", ModrinthClient.ParseBestVersion(Json(json), Fabric, "1.21.1")!.VersionId);
    }

    [Fact]
    public void ParseBestVersion_OtherGameVersionOrLoader_ReturnsNull()
    {
        var json = $"[{Version("a", "release", "2026-10-01T00:00:00Z", game: "1.20.1")},{Version("b", "release", "2026-10-01T00:00:00Z", loader: "forge")}]";

        Assert.Null(ModrinthClient.ParseBestVersion(Json(json), Fabric, "1.21.1"));
    }

    [Fact]
    public void ParseBestVersion_SpigotPluginOnPaper_IsAccepted()
    {
        var json = $"[{Version("a", "release", "2026-10-01T00:00:00Z", loader: "spigot")}]";

        Assert.NotNull(ModrinthClient.ParseBestVersion(Json(json), Paper, "1.21.1"));
    }

    [Fact]
    public void ParseBestVersion_Dependencies_KeepsOnlyRequiredProjects()
    {
        var dependencies = """
            [{"project_id":"P7dR8mSH","dependency_type":"required"},{"project_id":"opt","dependency_type":"optional"},
             {"project_id":null,"version_id":"v","dependency_type":"required"},{"project_id":"emb","dependency_type":"embedded"}]
            """;
        var json = $"[{Version("a", "release", "2026-10-01T00:00:00Z", dependencies: dependencies)}]";

        Assert.Equal(["P7dR8mSH"], ModrinthClient.ParseBestVersion(Json(json), Fabric, "1.21.1")!.RequiredProjectIds);
    }

    [Theory]
    [InlineData("http://cdn.modrinth.com/data/p/mod.jar", "mod.jar")]
    [InlineData("https://cdn.modrinth.com/data/p/mod.jar", "../mod.jar")]
    [InlineData("https://cdn.modrinth.com/data/p/mod.jar", "..\\\\mod.jar")]
    [InlineData("https://cdn.modrinth.com/data/p/mod.zip", "mod.zip")]
    public void ParseBestVersion_UnsafeFile_IsRejected(string url, string fileName)
    {
        var json = $"[{Version("a", "release", "2026-10-01T00:00:00Z", url: url, fileName: fileName)}]";

        Assert.Throws<InvalidDataException>(() => ModrinthClient.ParseBestVersion(Json(json), Fabric, "1.21.1"));
    }

    [Fact]
    public void ParseIdentifiedProjects_Map_ReturnsProjectIds()
    {
        var ids = ModrinthClient.ParseIdentifiedProjects(Json("""{"h1":{"id":"v1","project_id":"P7dR8mSH"},"h2":{"id":"v2","project_id":"AANobbMI"}}"""));

        Assert.Equal(["AANobbMI", "P7dR8mSH"], ids.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ParseIdentifiedVersions_IncompatibleDependencies_ListsProjectsOnly()
    {
        var versions = ModrinthClient.ParseIdentifiedVersions(Json("""
            {"h1":{"id":"v1","project_id":"AANobbMI","dependencies":[
              {"project_id":"YL57xq9U","dependency_type":"incompatible"},
              {"project_id":null,"version_id":"abc","dependency_type":"incompatible"},
              {"project_id":"P7dR8mSH","dependency_type":"required"}]}}
            """));

        Assert.Equal(["YL57xq9U"], versions["h1"].IncompatibleProjectIds);
    }

    [Fact]
    public void ParseBestVersion_IncompatibleDependency_IsListedApartFromRequired()
    {
        var json = $"[{Version("v1", "release", "2026-10-01T00:00:00Z", dependencies: """[{"project_id":"api","dependency_type":"required"},{"project_id":"optifine","dependency_type":"incompatible"}]""")}]";

        var version = ModrinthClient.ParseBestVersion(Json(json), Fabric, "1.21.1")!;

        Assert.Equal(["api"], version.RequiredProjectIds);
        Assert.Equal(["optifine"], version.IncompatibleProjectIds);
    }

    [Fact]
    public void ParseIdentifiedFiles_Map_ReturnsProjectPerHash()
    {
        var files = ModrinthClient.ParseIdentifiedFiles(Json("""{"h1":{"id":"v1","project_id":"P7dR8mSH"}}"""));

        Assert.Equal("P7dR8mSH", files["H1"]);
    }

    [Fact]
    public void ParseProjects_ServerSideUnsupported_IsClientOnly()
    {
        var projects = ModrinthClient.ParseProjects(Json("""
            [{"id":"AANobbMI","title":"Sodium","server_side":"unsupported"},
             {"id":"P7dR8mSH","title":"Fabric API","server_side":"required"},
             {"id":"gL7mEBC0","title":"","server_side":"unknown"}]
            """));

        Assert.Equal(
            [new ModProject("AANobbMI", "Sodium", true), new ModProject("P7dR8mSH", "Fabric API", false), new ModProject("gL7mEBC0", "gL7mEBC0", false)],
            projects);
    }

    [Fact]
    public void ProjectsUrl_SortsIds()
    {
        Assert.Equal(ModrinthClient.ProjectsUrl(["a", "b"]), ModrinthClient.ProjectsUrl(["b", "a"]));
    }
}
