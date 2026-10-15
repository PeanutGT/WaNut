using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaNut.Core.Instances;

/// <summary>
/// Reads and writes the server folders under instances/.
/// </summary>
public sealed class InstanceStore(LauncherPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,

        // A file people may open: write "+", quotes and Chinese names as they are, not as \u escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public string InstanceDir(string id) => Path.Combine(paths.InstancesDir, id);

    /// <summary>
    /// All servers, oldest first. Folders without a readable instance.json are skipped, never deleted.
    /// </summary>
    public IReadOnlyList<InstanceMetadata> LoadAll()
    {
        if (!Directory.Exists(paths.InstancesDir))
        {
            return [];
        }

        var instances = new List<InstanceMetadata>();
        foreach (var dir in Directory.EnumerateDirectories(paths.InstancesDir))
        {
            var file = Path.Combine(dir, InstanceMetadata.FileName);
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                var metadata = JsonSerializer.Deserialize<InstanceMetadata>(File.ReadAllText(file), JsonOptions);
                if (metadata is not null)
                {
                    instances.Add(metadata with { Id = Path.GetFileName(dir) });
                }
            }
            catch (JsonException)
            {
                // A damaged file must not hide the other servers.
            }
        }

        return instances.OrderBy(i => i.CreatedAt).ToList();
    }

    public void Save(InstanceMetadata metadata)
    {
        var dir = InstanceDir(metadata.Id);
        Directory.CreateDirectory(dir);
        AtomicFile.WriteAllText(Path.Combine(dir, InstanceMetadata.FileName), JsonSerializer.Serialize(metadata, JsonOptions));
    }

    /// <summary>
    /// Creates an empty folder for a new server and returns its id, e.g. "My Server" → "my-server", then "my-server-2".
    /// </summary>
    public string CreateFolder(string name)
    {
        Directory.CreateDirectory(paths.InstancesDir);
        var baseId = ToFolderName(name);
        for (var n = 1; ; n++)
        {
            var id = n == 1 ? baseId : $"{baseId}-{n}";
            var dir = InstanceDir(id);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                return id;
            }
        }
    }

    public static string ToFolderName(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_')
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var id = builder.ToString().Trim('-', '.');
        return id.Length == 0 ? "server" : id[..Math.Min(id.Length, 48)].TrimEnd('-', '.');
    }
}
