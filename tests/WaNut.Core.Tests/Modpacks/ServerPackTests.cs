using System.IO.Compression;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Modpacks;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Modpacks;

public sealed class ServerPackTests : IDisposable
{
    private const string ForgeManifest = """
        {"minecraft":{"version":"1.20.1","modLoaders":[{"id":"forge-47.2.0","primary":true}]},
         "manifestType":"minecraftModpack","name":"All the Things","version":"1.4","files":[]}
        """;

    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string ServerDir => Path.Combine(_dir.Path, "server");

    private string WriteZip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_dir.Path, "Cool Pack Server.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(content);
        }

        return path;
    }

    private static ModpackProblem ProblemOf(string path) => Assert.Throws<ModpackException>(() => ModpackReader.Read(path)).Problem;

    [Fact]
    public void Read_Manifest_ReadsForgeVersionsNameAndMods()
    {
        var pack = ModpackReader.Read(WriteZip(("manifest.json", ForgeManifest), ("mods/a.jar", "a"), ("mods/b.jar", "b"), ("config/a.toml", "")));

        Assert.True(pack.IsServerPack);
        Assert.Equal("All the Things", pack.Name);
        Assert.Equal("1.4", pack.Version);
        Assert.Equal("1.20.1", pack.MinecraftVersion);
        Assert.Equal(ServerType.Forge, pack.Type);
        Assert.Equal("47.2.0", pack.LoaderVersion);
        Assert.Equal(2, pack.ModCount);
        Assert.Equal(string.Empty, pack.ServerPackRoot);
    }

    [Fact]
    public void Read_VariablesTxt_ReadsNeoForge()
    {
        var pack = ModpackReader.Read(WriteZip(
            ("variables.txt", "MINECRAFT_VERSION=1.21.1\nMODLOADER=NeoForge\nMODLOADER_VERSION=\"21.1.77\"\n"),
            ("mods/a.jar", "a")));

        Assert.Equal(ServerType.NeoForge, pack.Type);
        Assert.Equal("1.21.1", pack.MinecraftVersion);
        Assert.Equal("21.1.77", pack.LoaderVersion);
        Assert.Equal("Cool Pack Server", pack.Name);
    }

    [Fact]
    public void Read_InstallerJarInWrapperFolder_FindsRootAndForgeVersion()
    {
        var pack = ModpackReader.Read(WriteZip(
            ("Pack-1.0/forge-1.19.2-43.3.0-installer.jar", "x"),
            ("Pack-1.0/mods/a.jar", "a"),
            ("Pack-1.0/startserver.bat", "@echo off")));

        Assert.Equal("Pack-1.0/", pack.ServerPackRoot);
        Assert.Equal(ServerType.Forge, pack.Type);
        Assert.Equal("1.19.2", pack.MinecraftVersion);
        Assert.Equal("43.3.0", pack.LoaderVersion);
        Assert.Equal(1, pack.ModCount);
    }

    [Fact]
    public void Read_NeoForgeLibraries_WorksOutMinecraftVersion()
    {
        var pack = ModpackReader.Read(WriteZip(("libraries/net/neoforged/neoforge/21.1.200/win_args.txt", ""), ("mods/a.jar", "a")));

        Assert.Equal(ServerType.NeoForge, pack.Type);
        Assert.Equal("1.21.1", pack.MinecraftVersion);
        Assert.Equal("21.1.200", pack.LoaderVersion);
    }

    [Fact]
    public void Read_FabricServerLauncher_ReadsFabric()
    {
        var pack = ModpackReader.Read(WriteZip(("fabric-server-mc.1.20.1-loader.0.15.11-launcher.1.0.1.jar", "x"), ("mods/a.jar", "a")));

        Assert.Equal(ServerType.Fabric, pack.Type);
        Assert.Equal("1.20.1", pack.MinecraftVersion);
        Assert.Equal("0.15.11", pack.LoaderVersion);
    }

    [Fact]
    public void Read_ServerStarterConfig_ReadsForge()
    {
        var yaml = """
            install:
              mcVersion: 1.16.5
              loaderVersion: "36.2.39"
              installerUrl: "https://maven.minecraftforge.net/net/minecraftforge/forge/1.16.5-36.2.39/forge-1.16.5-36.2.39-installer.jar"
            """;
        var pack = ModpackReader.Read(WriteZip(("server-setup-config.yaml", yaml), ("mods/a.jar", "a")));

        Assert.Equal(ServerType.Forge, pack.Type);
        Assert.Equal("1.16.5", pack.MinecraftVersion);
        Assert.Equal("36.2.39", pack.LoaderVersion);
    }

    [Fact]
    public void Read_StartScriptWithFullForgeVersion_SplitsIt()
    {
        var pack = ModpackReader.Read(WriteZip(("start.sh", "#!/bin/sh\nFORGE_VERSION=1.20.1-47.3.0\njava -jar x.jar\n"), ("mods/a.jar", "a")));

        Assert.Equal(ServerType.Forge, pack.Type);
        Assert.Equal("1.20.1", pack.MinecraftVersion);
        Assert.Equal("47.3.0", pack.LoaderVersion);
    }

    [Fact]
    public void Read_ManifestWinsOverOtherLoaderHints()
    {
        var pack = ModpackReader.Read(WriteZip(("manifest.json", ForgeManifest), ("variables.txt", "MODLOADER=Fabric\nMODLOADER_VERSION=0.15.0\n"), ("mods/a.jar", "a")));

        Assert.Equal(ServerType.Forge, pack.Type);
        Assert.Equal("47.2.0", pack.LoaderVersion);
    }

    [Fact]
    public void Read_CurseForgeGamePack_SaysToGetTheServerPack()
    {
        var manifest = ForgeManifest.Replace("\"files\":[]", "\"files\":[{\"projectID\":1,\"fileID\":2,\"required\":true}]");

        Assert.Equal(ModpackProblem.ClientPack, ProblemOf(WriteZip(("manifest.json", manifest), ("overrides/config/a.toml", ""))));
    }

    [Fact]
    public void Read_ModsWithoutAnyVersionHint_IsUnknownServerPack() =>
        Assert.Equal(ModpackProblem.UnknownServerPack, ProblemOf(WriteZip(("mods/a.jar", "a"), ("start.bat", "java -jar server.jar"))));

    [Fact]
    public void Read_ZipWithoutModsOrHints_IsNotAModpack() =>
        Assert.Equal(ModpackProblem.NotAModpack, ProblemOf(WriteZip(("world/level.dat", "x"))));

    [Fact]
    public void Read_QuiltPack_IsUnsupported() =>
        Assert.Equal(ModpackProblem.UnsupportedLoader, ProblemOf(WriteZip(("variables.txt", "MINECRAFT_VERSION=1.20.1\nMODLOADER=Quilt\n"), ("mods/a.jar", "a"))));

    [Fact]
    public async Task InstallAsync_CopiesPackFilesButNotStartFilesOrLoaderInstall()
    {
        Directory.CreateDirectory(ServerDir);
        File.WriteAllText(Path.Combine(ServerDir, "server.properties"), "white-list=true\nserver-port=25565\ndifficulty=easy\n");
        File.WriteAllText(Path.Combine(ServerDir, "eula.txt"), "eula=true");
        var path = WriteZip(
            ("Pack/manifest.json", ForgeManifest),
            ("Pack/mods/a.jar", "a"),
            ("Pack/config/a.toml", "cfg"),
            ("Pack/defaultconfigs/b.toml", "def"),
            ("Pack/forge-1.20.1-47.2.0-installer.jar", "installer"),
            ("Pack/startserver.bat", "run"),
            ("Pack/user_jvm_args.txt", "-Xmx4G"),
            ("Pack/libraries/net/minecraftforge/forge/1.20.1-47.2.0/win_args.txt", "args"),
            ("Pack/ops.json", "[{\"name\":\"stranger\"}]"),
            ("Pack/eula.txt", "eula=false"),
            ("Pack/server.properties", "white-list=false\nserver-port=1234\ndifficulty=hard\nallow-flight=true\n"));
        var installer = new ModpackInstaller(new FileDownloader(new HttpClient()));

        await installer.InstallAsync(ServerDir, path, ModpackReader.Read(path));

        Assert.Equal("a", File.ReadAllText(Path.Combine(ServerDir, "mods", "a.jar")));
        Assert.Equal("cfg", File.ReadAllText(Path.Combine(ServerDir, "config", "a.toml")));
        Assert.Equal("def", File.ReadAllText(Path.Combine(ServerDir, "defaultconfigs", "b.toml")));
        Assert.Equal("eula=true", File.ReadAllText(Path.Combine(ServerDir, "eula.txt")));
        foreach (var skipped in new[] { "forge-1.20.1-47.2.0-installer.jar", "startserver.bat", "user_jvm_args.txt", "libraries", "ops.json", "manifest.json" })
        {
            Assert.False(Path.Exists(Path.Combine(ServerDir, skipped)), skipped);
        }

        var properties = Core.Properties.PropertiesFile.Load(Path.Combine(ServerDir, "server.properties"));
        Assert.Equal("true", properties.Get("white-list"));
        Assert.Equal("25565", properties.Get("server-port"));
        Assert.Equal("hard", properties.Get("difficulty"));
        Assert.Equal("true", properties.Get("allow-flight"));
    }

    [Fact]
    public async Task InstallAsync_PathOutsideServerFolder_Throws()
    {
        Directory.CreateDirectory(ServerDir);
        var path = WriteZip(("manifest.json", ForgeManifest), ("mods/a.jar", "a"), ("config/../../evil.txt", "x"));
        var installer = new ModpackInstaller(new FileDownloader(new HttpClient()));

        var e = await Assert.ThrowsAsync<ModpackException>(() => installer.InstallAsync(ServerDir, path, ModpackReader.Read(path)));

        Assert.Equal(ModpackProblem.UnsafeFile, e.Problem);
        Assert.False(File.Exists(Path.Combine(_dir.Path, "evil.txt")));
    }
}