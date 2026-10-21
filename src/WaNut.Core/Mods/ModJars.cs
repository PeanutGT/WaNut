using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WaNut.Core.Mods;

/// <summary>
/// Reads mod ids from the jars in a mods folder (fabric.mod.json, quilt.mod.json, META-INF/mods.toml or
/// neoforge.mods.toml), so a mod id from a crash can be traced back to its file.
/// </summary>
public static partial class ModJars
{
    public static IReadOnlyList<ModJar> Read(string modsFolder)
    {
        if (!Directory.Exists(modsFolder))
        {
            return [];
        }

        return Directory.EnumerateFiles(modsFolder, "*.jar").Select(ReadJar).OfType<ModJar>().ToList();
    }

    /// <summary>Null when the file is not a readable mod jar.</summary>
    public static ModJar? ReadJar(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var name = Path.GetFileName(path);
            return FromFabric(zip, name) ?? FromQuilt(zip, name) ?? FromToml(zip, name);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The jars that hold a mod, matched by mod id, display name or file name.</summary>
    public static IReadOnlyList<ModJar> Find(IEnumerable<ModJar> jars, string idNameOrFile) =>
        jars.Where(j => string.Equals(j.Id, idNameOrFile, StringComparison.OrdinalIgnoreCase)
                || string.Equals(j.Name, idNameOrFile, StringComparison.OrdinalIgnoreCase)
                || string.Equals(j.FileName, idNameOrFile, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// Of several copies of one mod, the one to keep: the highest version, and among equal or unreadable versions the
    /// last in the list.
    /// </summary>
    public static ModJar Newest(IReadOnlyList<ModJar> copies) =>
        copies.Aggregate((best, next) => CompareVersions(next.Version, best.Version) >= 0 ? next : best);

    /// <summary>Compares "1.20.1-0.5.3" style versions number by number; text parts compare as text.</summary>
    public static int CompareVersions(string? a, string? b)
    {
        var left = VersionPart().Matches(a ?? string.Empty);
        var right = VersionPart().Matches(b ?? string.Empty);
        for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
        {
            var x = left[i].Value;
            var y = right[i].Value;
            var result = long.TryParse(x, out var nx) && long.TryParse(y, out var ny)
                ? nx.CompareTo(ny)
                : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            if (result != 0)
            {
                return result;
            }
        }

        return left.Count.CompareTo(right.Count);
    }

    private static ModJar? FromFabric(ZipArchive zip, string fileName)
    {
        if (zip.GetEntry("fabric.mod.json") is not { } entry)
        {
            return null;
        }

        using var stream = entry.Open();
        using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = json.RootElement;
        return String(root, "id") is { } id ? new ModJar(fileName, id, String(root, "name"), String(root, "version")) : null;
    }

    private static ModJar? FromQuilt(ZipArchive zip, string fileName)
    {
        if (zip.GetEntry("quilt.mod.json") is not { } entry)
        {
            return null;
        }

        using var stream = entry.Open();
        using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (!json.RootElement.TryGetProperty("quilt_loader", out var loader) || String(loader, "id") is not { } id)
        {
            return null;
        }

        var name = loader.TryGetProperty("metadata", out var metadata) ? String(metadata, "name") : null;
        return new ModJar(fileName, id, name, String(loader, "version"));
    }

    private static ModJar? FromToml(ZipArchive zip, string fileName)
    {
        if ((zip.GetEntry("META-INF/neoforge.mods.toml") ?? zip.GetEntry("META-INF/mods.toml")) is not { } entry)
        {
            return null;
        }

        using var reader = new StreamReader(entry.Open());
        var text = reader.ReadToEnd();

        // The first [[mods]] table is the jar's own mod; keys before it belong to the file as a whole.
        var start = text.IndexOf("[[mods]]", StringComparison.Ordinal);
        var mods = start < 0 ? text : text[start..];
        if (TomlString("modId", mods) is not { } id)
        {
            return null;
        }

        // "${file.jarVersion}" is filled in from the manifest at run time; it says nothing here.
        var version = TomlString("version", mods);
        return new ModJar(fileName, id, TomlString("displayName", mods), version is null || version.Contains("${", StringComparison.Ordinal) ? null : version);
    }

    private static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? TomlString(string key, string toml) =>
        Regex.Match(toml, $@"(?m)^\s*{key}\s*=\s*[""'](?<v>[^""'\r\n]*)[""']") is { Success: true } m ? m.Groups["v"].Value : null;

    [GeneratedRegex(@"\d+|[A-Za-z]+")]
    private static partial Regex VersionPart();
}
