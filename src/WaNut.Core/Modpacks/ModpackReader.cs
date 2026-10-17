using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;

namespace WaNut.Core.Modpacks;

/// <summary>
/// Reads the modrinth.index.json of a Modrinth .mrpack (format version 1) and keeps what a server needs: the
/// Minecraft and loader versions from "dependencies", and the files whose env.server is not "unsupported".
/// </summary>
public static partial class ModpackReader
{
    public const string IndexFileName = "modrinth.index.json";

    /// <summary>The hosts Modrinth allows in a published pack's download links.</summary>
    private static readonly string[] AllowedHosts = ["cdn.modrinth.com", "github.com", "raw.githubusercontent.com", "gitlab.com"];

    private static readonly string[] OtherLoaders = ["quilt-loader"];

    // The "dependencies" keys of the loaders WaNut can set up.
    private static readonly (string Key, ServerType Type)[] Loaders =
    [
        ("fabric-loader", ServerType.Fabric),
        ("forge", ServerType.Forge),
        ("neoforge", ServerType.NeoForge),
    ];

    /// <summary>
    /// Reads a Modrinth .mrpack, or a CurseForge server pack (any zip without a modrinth.index.json, see
    /// <see cref="ServerPackReader"/>).
    /// </summary>
    /// <exception cref="ModpackException">The file is not a usable modpack.</exception>
    public static Modpack Read(string mrpackPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(mrpackPath);
            if (zip.GetEntry(IndexFileName) is not { } entry)
            {
                return ServerPackReader.Read(zip, Path.GetFileNameWithoutExtension(mrpackPath));
            }

            using var stream = entry.Open();
            return Parse(stream);
        }
        catch (InvalidDataException e)
        {
            throw new ModpackException(ModpackProblem.NotAModpack, e.Message);
        }
    }

    /// <exception cref="ModpackException">The index is not a usable Minecraft pack.</exception>
    public static Modpack Parse(Stream indexJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(indexJson);
        }
        catch (JsonException e)
        {
            throw new ModpackException(ModpackProblem.NotAModpack, e.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("formatVersion", out var format) || format.ValueKind != JsonValueKind.Number || !format.TryGetInt32(out var formatVersion) || formatVersion != 1
                || GetString(root, "game") != "minecraft"
                || !root.TryGetProperty("dependencies", out var dependencies) || dependencies.ValueKind != JsonValueKind.Object)
            {
                throw new ModpackException(ModpackProblem.NotAModpack);
            }

            var minecraft = GetString(dependencies, "minecraft");
            if (minecraft is null || !IsPlainVersion(minecraft))
            {
                throw new ModpackException(ModpackProblem.NotAModpack, "No Minecraft version.");
            }

            if (OtherLoaders.FirstOrDefault(l => dependencies.TryGetProperty(l, out _)) is { } other)
            {
                throw new ModpackException(ModpackProblem.UnsupportedLoader, other);
            }

            var found = Loaders.Where(l => dependencies.TryGetProperty(l.Key, out _)).ToList();
            if (found.Count > 1)
            {
                throw new ModpackException(ModpackProblem.NotAModpack, "More than one mod loader.");
            }

            // The version goes into Fabric's install.properties or a Maven download link, so it must be one plain token.
            var type = found.Count == 0 ? ServerType.Vanilla : found[0].Type;
            var loader = found.Count == 0 ? null : GetString(dependencies, found[0].Key);
            if (found.Count > 0 && (loader is null || !IsPlainVersion(loader)))
            {
                throw new ModpackException(ModpackProblem.NotAModpack, "Unreadable mod loader version.");
            }

            var files = new List<ModpackFile>();
            var skipped = 0;
            if (root.TryGetProperty("files", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var file in list.EnumerateArray())
                {
                    if (file.ValueKind != JsonValueKind.Object)
                    {
                        throw new ModpackException(ModpackProblem.NotAModpack, "A file entry is not an object.");
                    }

                    if (IsServerUnsupported(file))
                    {
                        skipped++;
                        continue;
                    }

                    files.Add(ReadFile(file));
                }
            }

            var name = GetString(root, "name");
            return new Modpack(
                string.IsNullOrWhiteSpace(name) ? "Modpack" : name.Trim(),
                GetString(root, "versionId")?.Trim() ?? string.Empty,
                minecraft,
                type,
                loader,
                files,
                skipped);
        }
    }

    private static bool IsServerUnsupported(JsonElement file) =>
        file.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object && GetString(env, "server") == "unsupported";

    private static ModpackFile ReadFile(JsonElement file)
    {
        var path = GetString(file, "path");
        if (path is null || ModpackPaths.Resolve("root", path) is null)
        {
            throw new ModpackException(ModpackProblem.UnsafeFile, path);
        }

        var url = file.TryGetProperty("downloads", out var downloads) && downloads.ValueKind == JsonValueKind.Array
            ? downloads.EnumerateArray()
                .Where(d => d.ValueKind == JsonValueKind.String)
                .Select(d => Uri.TryCreate(d.GetString(), UriKind.Absolute, out var u) ? u : null)
                .FirstOrDefault(IsAllowedDownload)
            : null;
        if (url is null)
        {
            throw new ModpackException(ModpackProblem.UnsafeFile, path);
        }

        // The format requires both; SHA-512 is preferred because it is the stronger one.
        var hashes = file.TryGetProperty("hashes", out var h) && h.ValueKind == JsonValueKind.Object ? h : default;
        var sha512 = hashes.ValueKind == JsonValueKind.Object ? GetString(hashes, "sha512") : null;
        var sha1 = hashes.ValueKind == JsonValueKind.Object ? GetString(hashes, "sha1") : null;
        var hash = IsHex(sha512, 128) ? ExpectedHash.Sha512(sha512!)
            : IsHex(sha1, 40) ? ExpectedHash.Sha1(sha1!)
            : throw new ModpackException(ModpackProblem.UnsafeFile, path);
        return new ModpackFile(path.Replace('\\', '/'), url, hash);
    }

    public static bool IsAllowedDownload(Uri? url) =>
        url is not null && url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && AllowedHosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase);

    private static bool IsHex(string? value, int length) => value is not null && value.Length == length && value.All(Uri.IsHexDigit);

    internal static bool IsPlainVersion(string value) => PlainVersion().IsMatch(value);

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [GeneratedRegex(@"^[0-9A-Za-z][0-9A-Za-z.+_-]{0,63}$")]
    private static partial Regex PlainVersion();
}
