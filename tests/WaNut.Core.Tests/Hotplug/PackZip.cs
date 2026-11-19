using System.IO.Compression;

namespace WaNut.Core.Tests.Hotplug;

/// <summary>Builds small data pack zips for the hot-plug tests.</summary>
internal static class PackZip
{
    public const string Meta = """{"pack":{"pack_format":48,"description":"test"}}""";

    /// <summary>A zip at <paramref name="path"/> with pack.mcmeta (unless null) and the given entries.</summary>
    public static string Make(string path, string? meta = Meta, params string[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        if (meta is not null)
        {
            using var writer = new StreamWriter(zip.CreateEntry("pack.mcmeta").Open());
            writer.Write(meta);
        }

        foreach (var entry in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write("{}");
        }

        return path;
    }
}
