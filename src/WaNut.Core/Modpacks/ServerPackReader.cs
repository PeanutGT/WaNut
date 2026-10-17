using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using WaNut.Core.Instances;
using WaNut.Core.Versions;

namespace WaNut.Core.Modpacks;

/// <summary>
/// Reads a CurseForge server pack: a zip the pack's author made, holding mods/, config/ and the author's own start
/// files. There is no standard index, so the Minecraft version and mod loader come from whichever of these the pack
/// has, in this order: a CurseForge manifest.json, ServerPackCreator's variables.txt, ServerStarter's
/// server-setup-config.yaml, the loader's installer or library folders, and finally the start scripts.
/// </summary>
public static partial class ServerPackReader
{
    public const string ManifestFileName = "manifest.json";
    public const string ModsFolder = "mods/";

    // Wrapper folders ("Pack-1.0-Server/") are skipped up to this depth to find the folder holding the files.
    private const int MaxWrapperDepth = 2;

    // Start scripts are small; anything over this is not one worth reading.
    private const long MaxTextSize = 1024 * 1024;

    private sealed record Detected(string? Minecraft, string? Loader, string? LoaderVersion);

    /// <param name="fallbackName">The name to show when the pack doesn't carry one, usually the zip's file name.</param>
    /// <exception cref="ModpackException">The zip is a pack for the game, or doesn't say what server it needs.</exception>
    public static Modpack Read(ZipArchive zip, string fallbackName)
    {
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        var root = Root(names);
        var files = names.Where(n => n.StartsWith(root, StringComparison.Ordinal)).Select(n => n[root.Length..]).ToList();
        var modCount = files.Count(n => n.StartsWith(ModsFolder, StringComparison.OrdinalIgnoreCase)
            && n.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && n.IndexOf('/', ModsFolder.Length) < 0);

        string? name = null;
        string? version = null;
        var sources = new List<Detected>();
        if (Entry(zip, root + ManifestFileName) is { } manifestEntry)
        {
            var manifest = ReadManifest(manifestEntry, out name, out version, out var listsFiles);

            // A pack for the game lists CurseForge file ids to download and carries its mods only in overrides/.
            if (listsFiles && modCount == 0)
            {
                throw new ModpackException(ModpackProblem.ClientPack);
            }

            sources.Add(manifest);
        }

        sources.Add(FromVariables(ReadText(zip, root + "variables.txt")));
        sources.Add(FromServerStarter(ReadText(zip, root + "server-setup-config.yaml")));
        sources.Add(FromFileNames(files));
        foreach (var script in files.Where(IsStartScript))
        {
            sources.Add(FromScript(ReadText(zip, root + script)));
        }

        var found = Combine(sources);
        if (found.Loader == "quilt")
        {
            throw new ModpackException(ModpackProblem.UnsupportedLoader, "Quilt");
        }

        // Without mods and without any hint of a loader, the zip is something else, such as a world.
        if (found.Loader is not ("forge" or "neoforge" or "fabric"))
        {
            throw new ModpackException(modCount == 0 ? ModpackProblem.NotAModpack : ModpackProblem.UnknownServerPack);
        }

        var type = found.Loader switch
        {
            "forge" => ServerType.Forge,
            "neoforge" => ServerType.NeoForge,
            _ => ServerType.Fabric,
        };
        var loaderVersion = found.LoaderVersion;
        var minecraft = found.Minecraft;

        // Forge versions are often written with the Minecraft version in front ("1.20.1-47.2.0"); WaNut adds that itself.
        if (type == ServerType.Forge && loaderVersion is not null && ForgeFullVersion().Match(loaderVersion) is { Success: true } full)
        {
            minecraft ??= full.Groups["mc"].Value;
            loaderVersion = full.Groups["ver"].Value;
        }

        if (type == ServerType.NeoForge && loaderVersion is not null)
        {
            minecraft ??= NeoForgeClient.GameVersionOf(loaderVersion);
        }

        if (minecraft is null || !ModpackReader.IsPlainVersion(minecraft) || (loaderVersion is not null && !ModpackReader.IsPlainVersion(loaderVersion)))
        {
            throw new ModpackException(ModpackProblem.UnknownServerPack);
        }

        return new Modpack(
            string.IsNullOrWhiteSpace(name) ? fallbackName : name.Trim(),
            version?.Trim() ?? string.Empty,
            minecraft,
            type,
            loaderVersion,
            [],
            0,
            root,
            modCount);
    }

    /// <summary>The folder inside the zip that holds the server files: "" or one or two wrapper folders down.</summary>
    private static string Root(IReadOnlyList<string> names)
    {
        var root = string.Empty;
        for (var depth = 0; depth < MaxWrapperDepth; depth++)
        {
            var inside = names.Where(n => n.StartsWith(root, StringComparison.Ordinal) && n.Length > root.Length).Select(n => n[root.Length..]).ToList();
            var tops = inside.Select(n => n.Split('/')[0]).Distinct(StringComparer.Ordinal).ToList();

            // Only a single folder, with no file beside it, is a wrapper.
            if (tops.Count != 1 || inside.All(n => !n.Contains('/')) || inside.Any(n => n == tops[0]))
            {
                break;
            }

            root += tops[0] + "/";
        }

        return root;
    }

    /// <summary>Folds the sources together in order. A source naming a different loader than an earlier one is ignored.</summary>
    private static Detected Combine(IEnumerable<Detected> sources)
    {
        var result = new Detected(null, null, null);
        foreach (var source in sources)
        {
            if (source.Loader is not null && result.Loader is not null && source.Loader != result.Loader)
            {
                continue;
            }

            // A loader version only counts together with the loader it belongs to.
            var takeVersion = result.LoaderVersion is null && source.LoaderVersion is not null && source.Loader is not null;
            result = new Detected(
                result.Minecraft ?? source.Minecraft,
                result.Loader ?? source.Loader,
                takeVersion ? source.LoaderVersion : result.LoaderVersion);
        }

        return result;
    }
    private static Detected ReadManifest(ZipArchiveEntry entry, out string? name, out string? version, out bool listsFiles)
    {
        name = null;
        version = null;
        listsFiles = false;
        try
        {
            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new Detected(null, null, null);
            }

            name = GetString(root, "name");
            version = GetString(root, "version");
            listsFiles = root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array && files.GetArrayLength() > 0;
            if (!root.TryGetProperty("minecraft", out var minecraft) || minecraft.ValueKind != JsonValueKind.Object)
            {
                return new Detected(null, null, null);
            }

            // Loader ids look like "forge-47.2.0", "neoforge-21.1.77" or "fabric-0.15.7".
            string? loaderId = null;
            if (minecraft.TryGetProperty("modLoaders", out var loaders) && loaders.ValueKind == JsonValueKind.Array)
            {
                var all = loaders.EnumerateArray().Where(l => l.ValueKind == JsonValueKind.Object).ToList();
                var primary = all.FirstOrDefault(l => l.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True);
                loaderId = GetString(primary.ValueKind == JsonValueKind.Object ? primary : all.FirstOrDefault(), "id");
            }

            var gameVersion = GetString(minecraft, "version");
            var dash = loaderId?.IndexOf('-') ?? -1;
            if (dash <= 0)
            {
                return new Detected(gameVersion, null, null);
            }

            var loaderVersion = loaderId![(dash + 1)..];
            if (gameVersion is not null && loaderVersion.StartsWith(gameVersion + "-", StringComparison.Ordinal))
            {
                loaderVersion = loaderVersion[(gameVersion.Length + 1)..];
            }

            return new Detected(gameVersion, LoaderName(loaderId[..dash]), loaderVersion);
        }
        catch (JsonException)
        {
            return new Detected(null, null, null);
        }
    }

    /// <summary>ServerPackCreator's variables.txt: MINECRAFT_VERSION, MODLOADER and MODLOADER_VERSION lines.</summary>
    private static Detected FromVariables(string? text)
    {
        var values = Assignments(text);
        return new Detected(
            values.GetValueOrDefault("MINECRAFT_VERSION"),
            LoaderName(values.GetValueOrDefault("MODLOADER")),
            values.GetValueOrDefault("MODLOADER_VERSION"));
    }

    /// <summary>ServerStarter's server-setup-config.yaml: mcVersion, loaderVersion, and the loader from installerUrl.</summary>
    private static Detected FromServerStarter(string? text)
    {
        if (text is null)
        {
            return new Detected(null, null, null);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in YamlValue().Matches(text))
        {
            values.TryAdd(m.Groups["key"].Value, m.Groups["value"].Value.Trim().Trim('"', '\''));
        }

        var installer = values.GetValueOrDefault("installerUrl") ?? string.Empty;
        var loader = installer.Contains("neoforged", StringComparison.OrdinalIgnoreCase) ? "neoforge"
            : installer.Contains("minecraftforge", StringComparison.OrdinalIgnoreCase) ? "forge"
            : installer.Contains("fabricmc", StringComparison.OrdinalIgnoreCase) ? "fabric"
            : null;
        return new Detected(values.GetValueOrDefault("mcVersion"), loader, values.GetValueOrDefault("loaderVersion"));
    }

    /// <summary>Installer and server jars at the top, and the folders the loader's installer leaves under libraries/.</summary>
    private static Detected FromFileNames(IReadOnlyList<string> files)
    {
        string? minecraft = null;
        foreach (var file in files)
        {
            if (ForgeFile().Match(file) is { Success: true } forge)
            {
                return new Detected(forge.Groups["mc"].Value, "forge", forge.Groups["ver"].Value);
            }

            if (NeoForgeFile().Match(file) is { Success: true } neoForge)
            {
                return new Detected(null, "neoforge", neoForge.Groups["ver"].Value);
            }

            if (FabricJar().Match(file) is { Success: true } fabric)
            {
                return new Detected(fabric.Groups["mc"].Value, "fabric", fabric.Groups["ver"].Value);
            }

            minecraft ??= MinecraftFile().Match(file) is { Success: true } vanilla ? vanilla.Groups["mc"].Value : null;
        }

        var fabricLoader = files.Select(f => FabricLibrary().Match(f)).FirstOrDefault(m => m.Success);
        return fabricLoader is null ? new Detected(minecraft, null, null) : new Detected(minecraft, "fabric", fabricLoader.Groups["ver"].Value);
    }

    /// <summary>Version variables in a start script, such as "set NEOFORGE_VERSION=21.1.77" or "FORGE_VERSION=1.20.1-47.2.0".</summary>
    private static Detected FromScript(string? text)
    {
        var values = Assignments(text);
        var minecraft = values.GetValueOrDefault("MINECRAFT_VERSION") ?? values.GetValueOrDefault("MC_VERSION");
        if (values.GetValueOrDefault("NEOFORGE_VERSION") is { } neoForge)
        {
            return new Detected(minecraft, "neoforge", neoForge);
        }

        if (values.GetValueOrDefault("FORGE_VERSION") is { } forge)
        {
            return new Detected(minecraft, "forge", forge);
        }

        var fabric = values.GetValueOrDefault("FABRIC_LOADER_VERSION") ?? values.GetValueOrDefault("FABRIC_VERSION");
        return new Detected(minecraft, fabric is null ? null : "fabric", fabric);
    }

    /// <summary>The KEY=value lines of a text file, keys in upper case; the first one wins.</summary>
    private static Dictionary<string, string> Assignments(string? text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (text is not null)
        {
            foreach (Match m in Assignment().Matches(text))
            {
                values.TryAdd(m.Groups["key"].Value.ToUpperInvariant(), m.Groups["value"].Value);
            }
        }

        return values;
    }

    private static bool IsStartScript(string file) =>
        !file.Contains('/') && (file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".sh", StringComparison.OrdinalIgnoreCase));

    private static string? LoaderName(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "forge" or "minecraftforge" => "forge",
        "neoforge" or "neoforged" => "neoforge",
        "fabric" or "fabricmc" => "fabric",
        "quilt" => "quilt",
        _ => null,
    };

    private static ZipArchiveEntry? Entry(ZipArchive zip, string name) =>
        zip.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), name, StringComparison.OrdinalIgnoreCase));

    private static string? ReadText(ZipArchive zip, string name)
    {
        if (Entry(zip, name) is not { } entry || entry.Length > MaxTextSize)
        {
            return null;
        }

        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // KEY=value, KEY="value", set KEY=value, set "KEY=value" or export KEY=value, one per line.
    [GeneratedRegex(@"^[ \t]*(?:set[ \t]+""?|export[ \t]+)?(?<key>[A-Za-z_]+)[ \t]*=[ \t]*[""']?(?<value>[0-9A-Za-z][0-9A-Za-z.+_-]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex Assignment();

    [GeneratedRegex(@"^[ \t]*(?<key>mcVersion|loaderVersion|installerUrl)[ \t]*:[ \t]*(?<value>[^\r\n#]+)", RegexOptions.Multiline)]
    private static partial Regex YamlValue();

    [GeneratedRegex(@"^(?<mc>1\.\d+(?:\.\d+)?)-(?<ver>\d[0-9A-Za-z.+_-]*)$")]
    private static partial Regex ForgeFullVersion();

    [GeneratedRegex(@"^(?:forge-(?<mc>1\.\d+(?:\.\d+)?)-(?<ver>\d[0-9.]*)(?:-(?:installer|universal|shim|server))?\.jar|libraries/net/minecraftforge/forge/(?<mc>1\.\d+(?:\.\d+)?)-(?<ver>\d[0-9.]*)/.*)$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex ForgeFile();

    [GeneratedRegex(@"^(?:neoforge-(?<ver>\d+\.\d+\.\d+(?:\.\d+)?(?:-beta)?)(?:-(?:installer|server))?\.jar|libraries/net/neoforged/neoforge/(?<ver>\d+\.\d+\.\d+(?:\.\d+)?(?:-beta)?)/.*)$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex NeoForgeFile();

    [GeneratedRegex(@"^fabric-server-mc\.(?<mc>[0-9A-Za-z.]+)-loader\.(?<ver>[0-9A-Za-z.+]+)-launcher\.[0-9A-Za-z.]+\.jar$", RegexOptions.IgnoreCase)]
    private static partial Regex FabricJar();

    [GeneratedRegex(@"^libraries/net/fabricmc/fabric-loader/(?<ver>[0-9A-Za-z.+]+)/", RegexOptions.IgnoreCase)]
    private static partial Regex FabricLibrary();

    [GeneratedRegex(@"^(?:minecraft_server\.(?<mc>[0-9A-Za-z.]+)\.jar|libraries/net/minecraft/server/(?<mc>[0-9A-Za-z.]+)/.*)$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex MinecraftFile();
}