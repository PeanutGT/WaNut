using System.Text.Json;

namespace WaNut.Core.Players;

/// <summary>Reads usercache.json, where the server remembers the name and UUID of everyone who joined.</summary>
public static class UserCache
{
    public const string FileName = "usercache.json";

    /// <summary>The players in the server's usercache.json, by name; empty when there is none or it can't be read.</summary>
    public static IReadOnlyList<KnownPlayer> Read(string instanceDir)
    {
        var path = Path.Combine(instanceDir, FileName);
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (json.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var players = new List<KnownPlayer>();
            foreach (var item in json.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } text
                    && item.TryGetProperty("uuid", out var uuid) && Guid.TryParse(uuid.GetString(), out var id))
                {
                    players.Add(new KnownPlayer(text, id));
                }
            }

            return players.DistinctBy(p => p.Uuid).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
