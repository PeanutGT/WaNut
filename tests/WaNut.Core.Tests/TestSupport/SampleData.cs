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
}
