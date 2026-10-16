using System.Text.Json;

namespace WaNut.Core.Worlds;

/// <summary>
/// Finds the saves folders of the official launcher, the Modrinth App, Prism Launcher and CurseForge, at their
/// default places. Launchers installed elsewhere are reached with "Choose folder".
/// </summary>
public static class SaveLocations
{
    public static IReadOnlyList<SaveLocation> FindDefault() => Find(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>Every known instance whose game folder exists, official launcher first.</summary>
    public static IReadOnlyList<SaveLocation> Find(string appData, string userProfile)
    {
        var found = new List<SaveLocation>();
        var official = Path.Combine(appData, ".minecraft");
        if (Directory.Exists(official))
        {
            found.Add(new SaveLocation(LauncherKind.Official, null, official));
        }

        // The Modrinth App was called Theseus before its 0.8 release and kept that folder for a while.
        foreach (var root in new[] { Path.Combine(appData, "ModrinthApp", "profiles"), Path.Combine(appData, "com.modrinth.theseus", "profiles") })
        {
            found.AddRange(Instances(root).Select(dir => new SaveLocation(LauncherKind.ModrinthApp, Path.GetFileName(dir), dir)));
        }

        foreach (var dir in Instances(Path.Combine(appData, "PrismLauncher", "instances")))
        {
            // Prism keeps the game folder in ".minecraft", or "minecraft" in newer instances.
            var game = new[] { ".minecraft", "minecraft" }.Select(n => Path.Combine(dir, n)).FirstOrDefault(Directory.Exists);
            if (game is not null)
            {
                found.Add(new SaveLocation(LauncherKind.Prism, PrismName(dir), game));
            }
        }

        foreach (var dir in Instances(Path.Combine(userProfile, "curseforge", "minecraft", "Instances")))
        {
            found.Add(new SaveLocation(LauncherKind.CurseForge, CurseForgeName(dir), dir));
        }

        return found;
    }

    private static IEnumerable<string> Instances(string root)
    {
        try
        {
            return Directory.Exists(root) ? Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase).ToList() : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The name shown in Prism, from instance.cfg ("name=..."); the folder name when there is none.</summary>
    private static string PrismName(string dir)
    {
        try
        {
            var line = File.ReadLines(Path.Combine(dir, "instance.cfg")).FirstOrDefault(l => l.StartsWith("name=", StringComparison.Ordinal));
            if (line is not null && line.Length > 5)
            {
                return line[5..].Trim();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Falls back to the folder name.
        }

        return Path.GetFileName(dir);
    }

    /// <summary>The name shown in CurseForge, from minecraftinstance.json; the folder name when there is none.</summary>
    private static string CurseForgeName(string dir)
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(dir, "minecraftinstance.json"));
            using var json = JsonDocument.Parse(stream);
            if (json.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Falls back to the folder name.
        }

        return Path.GetFileName(dir);
    }
}
