using WaNut.Core.Worlds.Nbt;

namespace WaNut.Core.Worlds;

/// <summary>Reads level.dat and moves the single-player host's data between it and the server's player files.</summary>
public static class LevelData
{
    public const string FileName = "level.dat";

    public static WorldDetails ReadDetails(string levelDatPath, string fallbackName)
    {
        using var stream = File.OpenRead(levelDatPath);
        return ReadDetails(stream, fallbackName);
    }

    /// <exception cref="InvalidDataException">The file is not a readable level.dat.</exception>
    public static WorldDetails ReadDetails(Stream levelDat, string fallbackName)
    {
        var data = Data(NbtFile.Read(levelDat).Root);
        var name = data.String("LevelName");
        var lastPlayed = data.Number("LastPlayed");

        // ServerBrands lists every loader that has opened the world, Vanilla as "vanilla".
        var brands = (data["ServerBrands"]?.Value as NbtList)?.Items
            .Select(t => t.AsString?.Trim().ToLowerInvariant())
            .OfType<string>()
            .Where(b => b.Length > 0 && b != "vanilla")
            .Distinct()
            .ToList() ?? [];
        if (brands.Count == 0 && data.Number("WasModded") is 1)
        {
            brands.Add("modded");
        }

        return new WorldDetails(
            string.IsNullOrWhiteSpace(name) ? fallbackName : name,
            data.Compound("Version")?.String("Name"),
            lastPlayed is > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(lastPlayed.Value) : null,
            brands,
            data.Compound("Player") is not null);
    }

    /// <summary>The host's data from level.dat, as the root of a player file; null when the world has none.</summary>
    public static NbtCompound? ReadPlayer(string levelDatPath) => Data(NbtFile.Read(levelDatPath).Root).Compound("Player");

    /// <summary>Makes <paramref name="player"/> the single-player host's inventory and position.</summary>
    public static void WritePlayer(string levelDatPath, NbtCompound player)
    {
        var (rootName, root) = NbtFile.Read(levelDatPath);
        Data(root).Set("Player", NbtTag.Of(player));
        NbtFile.Write(levelDatPath, rootName, root);
    }

    private static NbtCompound Data(NbtCompound root) =>
        root.Compound("Data") ?? throw new InvalidDataException("level.dat has no Data compound.");
}
