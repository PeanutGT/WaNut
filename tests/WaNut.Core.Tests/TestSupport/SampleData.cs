using System.IO.Compression;
using System.Text;

namespace WaNut.Core.Tests.TestSupport;

internal static class SampleData
{
    public static string Manifest(params (string Id, string Type, string ReleaseTime, string Url, string Sha1)[] versions)
    {
        var entries = versions.Select(v =>
            $$"""{"id":"{{v.Id}}","type":"{{v.Type}}","url":"{{v.Url}}","time":"{{v.ReleaseTime}}","releaseTime":"{{v.ReleaseTime}}","sha1":"{{v.Sha1}}","complianceLevel":1}""");
        return $$"""{"latest":{"release":"x","snapshot":"y"},"versions":[{{string.Join(",", entries)}}]}""";
    }

    public static string VersionDetails(string jarUrl, string jarSha1, long size, int? javaMajor)
    {
        var java = javaMajor is null ? "" : $$""","javaVersion":{"component":"java-runtime-delta","majorVersion":{{javaMajor}}}""";
        return $$$"""{"id":"test","downloads":{"client":{"sha1":"aa","size":1,"url":"https://example.com/client.jar"},"server":{"sha1":"{{{jarSha1}}}","size":{{{size}}},"url":"{{{jarUrl}}}"}}{{{java}}}}""";
    }

    public static string AdoptiumLatest(int major, string zipUrl, string sha256, long size) =>
        $$"""
        [{
          "binary": {
            "architecture": "x64", "image_type": "jre", "os": "windows", "jvm_impl": "hotspot",
            "installer": {"checksum": "ff", "link": "https://example.com/jre.msi", "name": "OpenJDK{{major}}U-jre_x64_windows_hotspot.msi", "size": 1},
            "package": {"checksum": "{{sha256}}", "link": "{{zipUrl}}", "name": "OpenJDK{{major}}U-jre_x64_windows_hotspot.zip", "size": {{size}}}
          },
          "release_name": "jdk-{{major}}.0.1+1",
          "vendor": "eclipse"
        }]
        """;

    /// <summary>A zip shaped like a Temurin JRE: one top folder containing bin/java.exe.</summary>
    public static byte[] JreZip(int major)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry($"jdk-{major}.0.1+1-jre/bin/java.exe");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("fake java");
        }

        return buffer.ToArray();
    }

    /// <summary>Shaped like fill.papermc.io/v3/projects/paper (2026-10-14).</summary>
    public const string PaperProject = """
        {"project":{"id":"paper","name":"Paper"},
         "versions":{"26.3":["26.3","26.3-rc-3"],"1.21":["1.21.11","1.21.11-rc3","1.21.9-pre4","1.21.4","1.21.1"],"1.16":["1.16.5","1.16.4"]}}
        """;

    /// <summary>Shaped like fill.papermc.io/v3/projects/paper/versions/&lt;v&gt;/builds: newest first, any channel.</summary>
    public static string PaperBuilds(params (int Id, string Channel, string Url, string Sha256)[] builds)
    {
        var entries = builds.Select(b =>
            $$$$"""{"id":{{{{b.Id}}}},"time":"2026-10-14T12:00:00Z","channel":"{{{{b.Channel}}}}","commits":[{"sha":"aa","time":"2026-10-14T11:00:00Z","message":"x"}],"downloads":{"server:default":{"name":"paper-{{{{b.Id}}}}.jar","checksums":{"sha256":"{{{{b.Sha256}}}}"},"size":3,"url":"{{{{b.Url}}}}"}}}""");
        return $"[{string.Join(",", entries)}]";
    }

    /// <summary>Shaped like meta.fabricmc.net/v2/versions/game: snapshots first, newest first.</summary>
    public const string FabricGameVersions = """
        [{"version":"26.4-snapshot-4","stable":false},{"version":"26.3","stable":true},{"version":"1.21.4","stable":true},{"version":"1.21.4-rc1","stable":false},{"version":"1.16.5","stable":true}]
        """;

    public const string FabricLoaderVersions = """
        [{"separator":".","build":6,"maven":"net.fabricmc:fabric-loader:0.19.6-beta","version":"0.19.6-beta","stable":false},{"separator":".","build":5,"maven":"net.fabricmc:fabric-loader:0.19.5","version":"0.19.5","stable":true}]
        """;

    public const string FabricInstallerVersions = """
        [{"url":"https://maven.fabricmc.net/net/fabricmc/fabric-installer/1.1.2/fabric-installer-1.1.2.jar","maven":"net.fabricmc:fabric-installer:1.1.2","version":"1.1.2","stable":true},{"url":"https://maven.fabricmc.net/net/fabricmc/fabric-installer/1.1.1/fabric-installer-1.1.1.jar","maven":"net.fabricmc:fabric-installer:1.1.1","version":"1.1.1","stable":false}]
        """;

    /// <summary>A jar shaped like fabric-installer-&lt;v&gt;-server.jar: a manifest naming the server launcher.</summary>
    public static byte[] FabricServerLauncherJar()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("META-INF/MANIFEST.MF");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("Manifest-Version: 1.0\nMain-Class: net.fabricmc.installer.ServerLauncher\n");
        }

        return buffer.ToArray();
    }
}
