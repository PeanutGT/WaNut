using System.Text.Json;
using System.Text.RegularExpressions;

namespace WaNut.Core.Players;

public enum PlayerList
{
    Whitelist,
    Operators,
    Banned,
}

/// <summary>
/// The whitelist, operator list and ban list. Minecraft owns whitelist.json, ops.json and banned-players.json (each
/// entry needs the player's UUID, which only the server looks up), so WaNut reads those files and changes them
/// through console commands.
/// </summary>
public static partial class PlayerLists
{
    /// <summary>Ban and kick reasons are shown to the player; Minecraft cuts long ones off anyway.</summary>
    public const int MaxReasonLength = 100;

    public static string FileName(PlayerList list) => list switch
    {
        PlayerList.Whitelist => "whitelist.json",
        PlayerList.Operators => "ops.json",
        _ => "banned-players.json",
    };

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

    public static string AddCommand(PlayerList list, string name) => list switch
    {
        PlayerList.Whitelist => $"whitelist add {name}",
        PlayerList.Operators => $"op {name}",
        _ => $"ban {name}",
    };

    public static string RemoveCommand(PlayerList list, string name) => list switch
    {
        PlayerList.Whitelist => $"whitelist remove {name}",
        PlayerList.Operators => $"deop {name}",
        _ => $"pardon {name}",
    };

    /// <summary>Bans a player, with the reason they see when they try to join (none when empty).</summary>
    public static string BanCommand(string name, string? reason) => WithReason(AddCommand(PlayerList.Banned, name), reason);

    public static string KickCommand(string name, string? reason) => WithReason($"kick {name}", reason);

    /// <summary>
    /// The reason as it can go into a console command: one line, no control characters, at most
    /// <see cref="MaxReasonLength"/> characters. A line break would end the command and start another one.
    /// </summary>
    public static string CleanReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return string.Empty;
        }

        var oneLine = new string(reason.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        var collapsed = string.Join(' ', oneLine.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaxReasonLength ? collapsed : collapsed[..MaxReasonLength].TrimEnd();
    }

    /// <summary>Which list a queued command changes, for whom, and whether it adds or removes; null for other commands.</summary>
    public static (PlayerList List, string Name, bool Add)? ParseCommand(string command)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts switch
        {
            ["whitelist", "add", var name] => (PlayerList.Whitelist, name, true),
            ["whitelist", "remove", var name] => (PlayerList.Whitelist, name, false),
            ["op", var name] => (PlayerList.Operators, name, true),
            ["deop", var name] => (PlayerList.Operators, name, false),
            ["ban", var name, ..] => (PlayerList.Banned, name, true),
            ["pardon", var name] => (PlayerList.Banned, name, false),
            _ => null,
        };
    }

    /// <summary>
    /// The list as it will be once <paramref name="pending"/> commands run: names from the file, plus queued adds,
    /// minus queued removes. Names compare without case, as Minecraft does.
    /// </summary>
    public static IReadOnlyList<string> WithPending(IReadOnlyList<string> names, IReadOnlyList<string> pending, PlayerList list)
    {
        var result = names.ToList();
        foreach (var command in pending)
        {
            if (ParseCommand(command) is not { } change || change.List != list)
            {
                continue;
            }

            if (change.Add)
            {
                if (!result.Contains(change.Name, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(change.Name);
                }
            }
            else
            {
                result.RemoveAll(n => n.Equals(change.Name, StringComparison.OrdinalIgnoreCase));
            }
        }

        return result;
    }

    /// <summary>
    /// Adds a queued command, dropping an earlier one for the same player and list so adding and then removing
    /// a name before the next start leaves nothing queued for it. <paramref name="reason"/> goes with a ban.
    /// </summary>
    public static IReadOnlyList<string> Queue(
        IReadOnlyList<string> pending, PlayerList list, string name, bool add, IReadOnlyList<string> fileNames, string? reason = null)
    {
        var result = pending
            .Where(c => ParseCommand(c) is not { } change || change.List != list || !change.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Only queue a change the file does not already have.
        var inFile = fileNames.Contains(name, StringComparer.OrdinalIgnoreCase);
        if (add != inFile)
        {
            result.Add(!add ? RemoveCommand(list, name) : list == PlayerList.Banned ? BanCommand(name, reason) : AddCommand(list, name));
        }

        return result;
    }

    private static string WithReason(string command, string? reason) =>
        CleanReason(reason) is { Length: > 0 } clean ? $"{command} {clean}" : command;

    [GeneratedRegex("^[A-Za-z0-9_]{1,16}$")]
    private static partial Regex ValidName();
}
