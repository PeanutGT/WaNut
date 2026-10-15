using System.Text.Json;
using System.Text.RegularExpressions;

namespace WaNut.Core.Players;

public enum PlayerList
{
    Whitelist,
    Operators,
}

/// <summary>
/// The whitelist and operator list. Minecraft owns whitelist.json and ops.json (each entry needs the player's
/// UUID, which only the server looks up), so WaNut reads those files and changes them through console commands.
/// </summary>
public static partial class PlayerLists
{
    public static string FileName(PlayerList list) => list == PlayerList.Whitelist ? "whitelist.json" : "ops.json";

    /// <summary>Java Edition names: 1-16 letters, digits or underscores.</summary>
    public static bool IsValidName(string name) => ValidName().IsMatch(name);

    /// <summary>The names in the list's file, in file order. A missing or damaged file reads as empty.</summary>
    public static IReadOnlyList<string> ReadNames(string instanceDir, PlayerList list)
    {
        var path = Path.Combine(instanceDir, FileName(list));
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return document.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                .Select(e => e.GetProperty("name").GetString()!)
                .ToList();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return [];
        }
    }

    public static string AddCommand(PlayerList list, string name) =>
        list == PlayerList.Whitelist ? $"whitelist add {name}" : $"op {name}";

    public static string RemoveCommand(PlayerList list, string name) =>
        list == PlayerList.Whitelist ? $"whitelist remove {name}" : $"deop {name}";

    /// <summary>
    /// The list as it will be once <paramref name="pending"/> commands run: names from the file, plus queued adds,
    /// minus queued removes. Names compare without case, as Minecraft does.
    /// </summary>
    public static IReadOnlyList<string> WithPending(IReadOnlyList<string> names, IReadOnlyList<string> pending, PlayerList list)
    {
        var result = names.ToList();
        foreach (var command in pending)
        {
            var parts = command.Split(' ');
            var name = parts[^1];
            if (command.Equals(AddCommand(list, name), StringComparison.Ordinal))
            {
                if (!result.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(name);
                }
            }
            else if (command.Equals(RemoveCommand(list, name), StringComparison.Ordinal))
            {
                result.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
        }

        return result;
    }

    /// <summary>
    /// Adds a queued command, dropping an earlier one for the same player and list so adding and then removing
    /// a name before the next start leaves nothing queued for it.
    /// </summary>
    public static IReadOnlyList<string> Queue(IReadOnlyList<string> pending, PlayerList list, string name, bool add, IReadOnlyList<string> fileNames)
    {
        var addCommand = AddCommand(list, name);
        var remove = RemoveCommand(list, name);
        var result = pending
            .Where(c => !c.Equals(addCommand, StringComparison.OrdinalIgnoreCase) && !c.Equals(remove, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Only queue a change the file does not already have.
        var inFile = fileNames.Contains(name, StringComparer.OrdinalIgnoreCase);
        if (add != inFile)
        {
            result.Add(add ? addCommand : remove);
        }

        return result;
    }

    [GeneratedRegex("^[A-Za-z0-9_]{1,16}$")]
    private static partial Regex ValidName();
}
